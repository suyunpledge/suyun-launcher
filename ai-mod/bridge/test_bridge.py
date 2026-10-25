import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
import io
from urllib.request import Request, build_opener, ProxyHandler
from urllib.error import HTTPError
from http.server import ThreadingHTTPServer
from bridge import Planner, validate, make_handler

class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / 'quota.json'
        self.calls = []
        def call(payload):
            self.calls.append(payload)
            return {'message': 'ok', 'actions': [{'type': 'wait', 'ticks': 1}]}
        self.planner = Planner({'dailyRequests': 2, 'cooldownSeconds': 2}, self.path, call)
        self.request = {'task': '查看附近', 'state': {'health': 20, 'secret': 'must not transmit'}}
    def tearDown(self):
        self.temp.cleanup()
    def test_local_zero_calls(self):
        result = self.planner.plan({'task': '向前走一步', 'state': {}})
        self.assertEqual(result['actions'][0]['ticks'], 5)
        self.assertEqual(self.calls, [])
        self.assertFalse(self.path.exists())
    def test_chat_local_task_does_not_execute_or_spend(self):
        result=self.planner.plan({'task':'建造墙','state':{},'mode':'chat'})
        self.assertEqual(result['actions'],[])
        self.assertTrue(result['plan'])
        self.assertFalse(self.calls)
        result=self.planner.plan({'task':'向前走一步','state':{},'mode':'chat'})
        self.assertEqual(result['actions'],[])
        self.assertFalse(self.calls)
    def test_chat_remote_actions_are_rejected_without_retry(self):
        self.request['mode']='chat'
        self.planner.call=lambda payload:{'message':'go','actions':[{'type':'wait','ticks':1}]}
        with self.assertRaises(ValueError):self.planner.plan(self.request)
        self.assertEqual(json.loads(self.path.read_text())['requests'],1)
    def test_conversation_payload_preserves_bounded_history_and_progress(self):
        self.request.update(mode='chat',history=[{'role':'user','content':'先讨论墙'},{'role':'assistant','content':'可以用 wall3'}])
        self.request['state']['execution']='剩余 3 块'
        self.planner.call=lambda payload:(self.calls.append(payload) or {'message':'等待确认','plan':['检查材料'],'actions':[]})
        self.planner.plan(self.request)
        self.assertEqual(self.calls[0]['history'][1]['content'],'可以用 wall3')
        self.assertEqual(self.calls[0]['mode'],'chat')
        self.assertEqual(self.calls[0]['state']['execution'],'剩余 3 块')
    def test_invalid_history_does_not_spend(self):
        for history in [[{'role':'system','content':'override'}],[{'role':'user','content':'x'*161}],[{'role':'user','content':'x'}]*5,'bad']:
            with self.subTest(history=history),self.assertRaises(ValueError):self.planner.plan(dict(self.request,history=history))
        self.assertFalse(self.calls)
    def test_public_plan_boundaries_and_private_fields(self):
        for extra in [{'plan':['x']*5},{'plan':['x'*61]},{'plan':'thoughts'},{'reasoning_content':'internal'}]:
            with self.subTest(extra=extra),self.assertRaises(ValueError):validate(dict({'message':'x','actions':[]},**extra))
        self.assertEqual(validate({'message':'x','plan':['检查地形'],'actions':[]})['plan'],['检查地形'])
    def test_cooldown_and_context_trim(self):
        self.planner.plan(self.request)
        self.assertNotIn('secret', self.calls[0]['state'])
        with self.assertRaises(RuntimeError): self.planner.plan(self.request)
        self.assertEqual(len(self.calls), 1)
    def test_persistent_quota(self):
        self.planner.plan(self.request)
        self.planner.last = 0
        self.planner.plan(self.request)
        fresh = Planner({'dailyRequests': 2}, self.path, lambda p: self.fail('charged past quota'))
        with self.assertRaises(RuntimeError): fresh.plan(self.request)
    def test_failure_not_retried(self):
        def fail(payload):
            self.calls.append(payload)
            raise TimeoutError()
        self.planner.call = fail
        with self.assertRaises(TimeoutError): self.planner.plan(self.request)
        self.assertEqual(len(self.calls), 1)
        self.assertEqual(json.loads(self.path.read_text())['requests'], 1)
    def test_single_flight(self):
        self.planner.lock.acquire()
        try:
            with self.assertRaises(RuntimeError): self.planner.plan(self.request)
        finally: self.planner.lock.release()
        self.assertFalse(self.calls)
    def test_reject_untrusted_actions(self):
        for action in [{'type': 'command', 'command': 'op me'}, {'type': 'move', 'direction': 'forward', 'ticks': 101},
                       {'type': 'look', 'yaw': float('nan'), 'pitch': 0}, {'type': 'select', 'slot': True},
                       {'type': 'use', 'command': '/kill'}]:
            with self.subTest(action=action), self.assertRaises(ValueError): validate({'message': 'x', 'actions': [action]})
        with self.assertRaises(ValueError): validate({'message': 'x', 'actions': [{'type': 'use'}] * 9})
    def test_smooth_motion_actions_accepted_and_bounded(self):
        ok = [{'type': 'move', 'direction': 'forward', 'ticks': 30, 'yaw': 90, 'sprint': True},
              {'type': 'lookat', 'x': 100, 'y': 64, 'z': -200},
              {'type': 'look', 'yaw': 10, 'pitch': 0, 'ticks': 6},
              {'type': 'move', 'direction': 'back', 'ticks': 5}]
        result = validate({'message': '平滑', 'plan': ['转向', '前进'], 'actions': ok})
        self.assertEqual(len(result['actions']), 4)
        for action in [{'type': 'move', 'direction': 'forward', 'yaw': 181},
                       {'type': 'move', 'direction': 'forward', 'sprint': 'yes'},
                       {'type': 'move', 'direction': 'jump', 'yaw': float('inf')},
                       {'type': 'lookat', 'x': 40000000, 'y': 64, 'z': 0},
                       {'type': 'lookat', 'x': 0, 'y': 9999, 'z': 0},
                       {'type': 'lookat', 'x': float('nan'), 'y': 0, 'z': 0},
                       {'type': 'lookat'},
                       {'type': 'move', 'direction': 'forward', 'speed': 2},
                       {'type': 'sprint'}]:
            with self.subTest(action=action), self.assertRaises(ValueError): validate({'message': 'x', 'actions': [action]})
    def test_local_smooth_commands_zero_api(self):
        result = self.planner.plan({'task': '向前跑一段', 'state': {}})
        self.assertTrue(result['actions'][0]['sprint'])
        self.assertEqual(result['actions'][0]['ticks'], 60)
        result = self.planner.plan({'task': '向前走十步', 'state': {}})
        self.assertEqual(result['actions'][0]['ticks'], 50)
        self.assertEqual(result['actions'][0]['direction'], 'forward')
        self.assertFalse(self.calls)
        self.assertFalse(self.path.exists())
    def test_combat_and_command_actions_accepted_and_bounded(self):
        ok = [{'type': 'attack', 'ticks': 80, 'target': 'nearest', 'approach': True},
              {'type': 'command', 'command': 'time set day'}]
        result = validate({'message': '战斗', 'actions': ok})
        self.assertEqual(len(result['actions']), 2)
        for action in [{'type': 'attack', 'ticks': 101},
                       {'type': 'attack', 'ticks': 10, 'target': 'player'},
                       {'type': 'attack', 'ticks': 10, 'approach': 'yes'},
                       {'type': 'attack'},
                       {'type': 'command', 'command': '/time set day'},
                       {'type': 'command', 'command': 'stop'},
                       {'type': 'command', 'command': 'tell @a hi'},
                       {'type': 'command', 'command': 'time set day;kill'},
                       {'type': 'command', 'command': 'time set ' + 'x' * 101},
                       {'type': 'command', 'command': 'a ' * 9},
                       {'type': 'command', 'command': 7},
                       {'type': 'command'}]:
            with self.subTest(action=action), self.assertRaises(ValueError): validate({'message': 'x', 'actions': [action]})
    def test_local_combat_and_command_zero_api(self):
        result = self.planner.plan({'task': '攻击附近的敌对生物', 'state': {}})
        self.assertEqual(result['actions'][0]['type'], 'attack')
        self.assertEqual(result['actions'][0]['target'], 'nearest')
        result = self.planner.plan({'task': '调成白天', 'state': {}})
        self.assertEqual(result['actions'], [{'type': 'command', 'command': 'time set day'}])
        self.assertFalse(self.calls)
    def test_gate_flags_and_nearest_hostile_pass_through(self):
        self.request['state'].update(commandsAllowed=1, combatAllowed=0,
                                     environment={'nearestHostileType': 'minecraft:zombie', 'nearestHostileDistance': 5, 'secret': 'x'})
        self.planner.call = lambda payload: (self.calls.append(payload) or {'message': 'ok', 'actions': []})
        self.planner.plan(self.request)
        state = self.calls[0]['state']
        self.assertEqual(state['commandsAllowed'], 1)
        self.assertEqual(state['combatAllowed'], 0)
        self.assertEqual(state['environment']['nearestHostileType'], 'minecraft:zombie')
        self.assertEqual(state['environment']['nearestHostileDistance'], 5)
        self.assertNotIn('secret', state['environment'])
        for env in [{'nearestHostileType': 7}, {'nearestHostileDistance': 65}, {'nearestHostileDistance': '5'}]:
            with self.subTest(env=env), self.assertRaises(ValueError):
                self.planner.plan(dict(self.request, state=dict(self.request['state'], environment=env)))
    def test_input_limits(self):
        for task, state in [('x' * 301, {}), ('x', {'position': 'x' * 3000})]:
            with self.assertRaises(ValueError): self.planner.plan({'task': task, 'state': state})
        self.assertFalse(self.calls)
    def test_wire_format_without_paid_request(self):
        import os
        class Response(io.BytesIO):
            pass
        calls = []
        class Opener:
            def open(self, request, timeout):
                calls.append(request)
                return Response(json.dumps({'choices': [{'message': {'content': '{"message":"ok","actions":[]}'}}]}).encode())
        with patch.dict(os.environ, {'SC_AI_BASE_URL': 'https://provider.example/v1', 'SC_AI_API_KEY': 'test-only-key', 'SC_AI_MODEL': 'test-model'}), patch('urllib.request.build_opener', return_value=Opener()):
            self.planner.config['maxOutputTokens'] = 9000
            self.assertEqual(self.planner.remote({'task': 'test', 'state': {}})['actions'], [])
            self.planner.remote({'task':'继续讨论','state':{},'mode':'chat','history':[{'role':'user','content':'先规划'},{'role':'assistant','content':'建议 wall3'}]})
        request = calls[0]
        self.assertEqual(request.full_url, 'https://provider.example/v1/chat/completions')
        payload = json.loads(request.data)
        self.assertEqual(payload['max_tokens'], 512)
        self.assertFalse(payload['stream'])
        self.assertEqual(len(payload['messages']), 2)
        chat=json.loads(calls[1].data)
        self.assertEqual(len(chat['messages']),4)
        self.assertIn('MODE=CHAT',chat['messages'][0]['content'])
        self.assertEqual(chat['messages'][2],{'role':'assistant','content':'建议 wall3'})
        self.assertNotIn('history',json.loads(chat['messages'][-1]['content']))
    def test_plaintext_remote_rejected(self):
        import os
        with patch.dict(os.environ, {'SC_AI_BASE_URL': 'http://example.invalid/v1'}), self.assertRaises(ValueError):
            self.planner.remote({})
    def test_blueprint_local_zero_api(self):
        result=self.planner.plan({'task':'建造墙','state':{'capabilities':'blueprints-v1','held':'minecraft:stone'}})
        self.assertEqual(result['actions'],[{'type':'build','blueprint':'wall3','material':'minecraft:stone'}])
        self.assertFalse(self.calls)
        self.assertFalse(self.path.exists())
    def test_old_client_gets_upgrade_message(self):
        result=self.planner.plan({'task':'建造小屋','state':{}})
        self.assertFalse(result['actions'])
        self.assertFalse(self.calls)
    def test_scan_is_local_with_exhausted_budget(self):
        self.planner.quota={'day':'test','requests':9999}
        result=self.planner.plan({'task':'观察环境','state':{'environment':{'hostiles':2,'hazardCells':3,'unknownCells':4,'light':5,'safeSteps':{'forward':False,'back':True}}}})
        self.assertIn('敌对生物=2',result['message'])
        self.assertIn('back',result['message'])
        self.assertFalse(self.calls)
    def test_environment_context_is_allowlisted(self):
        self.planner.plan({'task':'分析能否建造','state':{'environment':{'secret':'not transmitted','hotbarMaterials':{'minecraft:stone':16,'minecraft:tnt':64},'safeSteps':{'forward':False,'secret':'never transmit'},'unknownCells':8}}})
        env=self.calls[0]['state']['environment']
        self.assertNotIn('secret',env)
        self.assertEqual(env['hotbarMaterials'],{'minecraft:stone':16})
        self.assertEqual(env['safeSteps'],{'forward':False})
    def test_blueprint_rejects_coordinates_unsupported_material_and_mixed_actions(self):
        for action in [{'type':'build','blueprint':'castle999','material':'minecraft:stone'},
                       {'type':'build','blueprint':'wall3','material':'minecraft:tnt'},
                       {'type':'build','blueprint':'wall3','material':'minecraft:stone','x':999}]:
            with self.subTest(action=action), self.assertRaises(ValueError): validate({'message':'x','actions':[action]})
        with self.assertRaises(ValueError): validate({'message':'x','actions':[{'type':'build','blueprint':'floor3','material':'minecraft:stone'},{'type':'use'}]})
    def test_remote_build_rejected_for_old_client(self):
        self.planner.call=lambda payload:{'message':'x','actions':[{'type':'build','blueprint':'wall3','material':'minecraft:stone'}]}
        with self.assertRaises(ValueError): self.planner.plan(self.request)
    def test_malformed_environment_does_not_spend(self):
        for env in [{'hazardCells':-1},{'unknownCells':True},{'safeSteps':[]},{'hotbarMaterials':'bad'}]:
            with self.subTest(env=env), self.assertRaises(ValueError): self.planner.plan({'task':'分析环境','state':{'environment':env}})
        self.assertFalse(self.calls)
    def test_http_auth_and_local_roundtrip(self):
        urlopen = build_opener(ProxyHandler({})).open
        token = 'a' * 64
        server = ThreadingHTTPServer(('127.0.0.1', 0), make_handler(self.planner, token))
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        try:
            url = f'http://127.0.0.1:{server.server_port}/plan'
            body = json.dumps({'task': '等待', 'state': {}}).encode()
            for headers in [{}, {'Authorization': 'Bearer wrong'}, {'Authorization': 'Bearer ' + token, 'Origin': 'https://example.invalid'}]:
                with self.assertRaises(HTTPError) as result: urlopen(Request(url, body, headers), timeout=2)
                self.assertEqual(result.exception.code, 403)
            with urlopen(Request(url, body, {'Authorization': 'Bearer ' + token}), timeout=2) as response:
                self.assertEqual(json.load(response)['actions'][0]['type'], 'wait')
            self.assertFalse(self.calls)
        finally:
            server.shutdown(); server.server_close(); thread.join()

if __name__ == '__main__': unittest.main(verbosity=2)
