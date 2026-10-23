package cn.suyuncraft.ai;

import com.google.gson.*;
import com.mojang.brigadier.arguments.StringArgumentType;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.text.Text;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.BlockPos;
import java.net.URI;
import java.net.HttpURLConnection;
import java.net.Proxy;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import static net.fabricmc.fabric.api.client.command.v2.ClientCommandManager.*;

public final class AiClient implements ClientModInitializer {
    private static AiClient instance;
    private final ConversationLog conversation=new ConversationLog();
    private String phase="就绪";
    private Object dialogWorld;
    private boolean openDialog;
    private final LocalBuilder builder = new LocalBuilder(this::say);
    public static void serverBlockUpdate(BlockPos pos, net.minecraft.block.BlockState state) {
        if (instance != null) instance.builder.serverUpdate(pos,state);
    }
    public static void renderWorkHud(net.minecraft.client.gui.DrawContext ctx) {
        if(instance==null)return;
        var ai=instance; var mc=MinecraftClient.getInstance();
        if(mc.player==null || mc.options.hudHidden || mc.currentScreen instanceof AiChatScreen || ai.pending==null && ai.active==null && ai.queue.isEmpty() && !ai.builder.hasPlan())return;
        int w=Math.max(20,Math.min(360,mc.getWindow().getScaledWidth()-16));
        ctx.fill(6,6,w+10,34,0xB0101824);
        ctx.drawTextWithShadow(mc.textRenderer,mc.textRenderer.trimToWidth("Suyun AI · "+(ai.pending!=null?"正在分析/等待接口":ai.phase),w),10,9,0xA8E8B8);
        ctx.drawTextWithShadow(mc.textRenderer,mc.textRenderer.trimToWidth(ai.builder.hasPlan()?ai.builder.status():"剩余动作 "+(ai.queue.size()+(ai.active==null?0:1))+" · /scai 打开对话",w),10,22,0xFFFFFF);
    }
    private final ExecutorService network = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "suyuncraft-ai-network"); t.setDaemon(true); return t;
    });
    private volatile HttpURLConnection connection;
    private final ArrayDeque<JsonObject> queue = new ArrayDeque<>();
    private CompletableFuture<?> pending;
    private JsonObject active;
    private BlockPos actionTarget;
    private int remaining;
    private volatile int generation;
    private Object world;
    private JsonObject config;

    public void onInitializeClient() {
        instance=this;
        ClientCommandRegistrationCallback.EVENT.register((d, r) -> d.register(literal("scai")
            .executes(c->{openDialog=true;return 1;})
            .then(literal("chat").executes(c->{openDialog=true;return 1;}))
            .then(literal("ask").then(argument("task", StringArgumentType.greedyString()).executes(c -> {
                ask(MinecraftClient.getInstance(), StringArgumentType.getString(c, "task")); return 1;
            })))
            .then(literal("stop").executes(c -> { stop(MinecraftClient.getInstance()); say("已停止（不会继续调用 API）"); return 1; }))
            .then(literal("scan").executes(c -> { var mc=MinecraftClient.getInstance(); if(mc.player!=null && mc.world!=null) say(EnvironmentProbe.observe(mc).toString()); return 1; }))
            .then(literal("build").then(argument("blueprint",StringArgumentType.greedyString()).executes(c -> {
                directBuild(MinecraftClient.getInstance(),StringArgumentType.getString(c,"blueprint")); return 1;
            })))
            .then(literal("confirm").executes(c -> { beginBuild(); return 1; }))
            .then(literal("resume").executes(c -> { beginBuild(); return 1; }))
            .then(literal("status").executes(c -> { say("请求中=" + (pending != null) + "，剩余动作=" + (queue.size() + (active == null ? 0 : 1))+"；"+builder.status()); return 1; }))));
        ClientTickEvents.END_CLIENT_TICK.register(this::tick);
    }
    private void say(String message) {
        if(message==null)message="状态更新";
        phase=message.substring(0,Math.min(120,message.length()));
        conversation.add("status",message);
        var player = MinecraftClient.getInstance().player;
        if (player != null) player.sendMessage(Text.literal("[Suyun AI] " + message), false);
    }
    List<ConversationLog.Entry> dialogLines() { return conversation.lines(); }
    String dialogStatus() { return (pending!=null?"正在分析与规划（等待接口）":phase)+" | "+(builder.hasPlan()?builder.status():"动作剩余="+(queue.size()+(active==null?0:1))); }
    void clearDialog() {
        generation++; if(pending!=null)pending.cancel(true); if(connection!=null)connection.disconnect(); pending=null;
        conversation.clear(); phase="对话已清空";
    }
    void stopDialog() { stop(MinecraftClient.getInstance());say("已停止全部任务"); }
    void confirmDialog() { if(builder.hasPlan())beginBuild(); else say(active!=null || !queue.isEmpty()?"已返回游戏，继续未完成动作":"当前没有可继续的任务"); }
    void sendDialog(String text,boolean work) {
        var mc=MinecraftClient.getInstance();
        if(text.equals("停止") || text.equals("取消")){stopDialog();return;}
        if(text.equals("继续") || text.equals("确认")) {
            if(work){mc.setScreen(null);confirmDialog();}else say("对话模式不执行动作；请点击「确认/继续」或「交给AI执行」。");
            return;
        }
        if(text.equals("查看状态")){say(dialogStatus());return;}
        request(mc,text,work);
    }
    private void release(MinecraftClient mc) {
        mc.options.forwardKey.setPressed(false); mc.options.backKey.setPressed(false);
        mc.options.leftKey.setPressed(false); mc.options.rightKey.setPressed(false);
        mc.options.jumpKey.setPressed(false);
        if (mc.interactionManager != null) mc.interactionManager.cancelBlockBreaking();
    }
    private void stop(MinecraftClient mc) {
        builder.stop();
        generation++;
        if (pending != null) pending.cancel(true);
        if (connection != null) connection.disconnect();
        pending = null; queue.clear(); active = null; remaining = 0; release(mc);
    }
    private void directBuild(MinecraftClient mc,String input) {
        if (mc.player==null || mc.world==null) return;
        if (pending!=null || active!=null || !queue.isEmpty() || builder.hasPlan()) { say("先 /scai stop 结束当前任务"); return; }
        try {
            config=readConfig(); world=mc.world;
            String[] args=input.trim().split("\\s+");
            if(args.length>2) throw new IllegalArgumentException("用法：/scai build floor3 [minecraft:cobblestone]");
            String material=args.length==2?args[1]:net.minecraft.registry.Registries.ITEM.getId(mc.player.getMainHandStack().getItem()).toString();
            if(!material.contains(":")) material="minecraft:"+material;
            builder.preview(mc,args[0],material);
        } catch(Exception e) { say(e.getMessage()); }
    }
    private void beginBuild() {
        try {
            var mc=MinecraftClient.getInstance();
            if(mc.player==null || mc.world==null) return;
            config=readConfig();
            builder.begin(mc,config.has("allowWorldInteraction") && config.get("allowWorldInteraction").getAsBoolean());
        } catch(Exception e) { say(e.getMessage()); }
    }
    private JsonObject readConfig() throws Exception {
        Path path = FabricLoader.getInstance().getConfigDir().resolve("suyuncraft-ai.json");
        if (!Files.exists(path)) throw new IllegalStateException("请先从启动器安装 AI 模组并启动本地桥接服务");
        JsonObject c = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        URI endpoint = URI.create(c.get("endpoint").getAsString());
        if (!"http".equals(endpoint.getScheme()) || !"127.0.0.1".equals(endpoint.getHost()) || endpoint.getPort() != 18765 || !"/plan".equals(endpoint.getPath()))
            throw new IllegalArgumentException("模组仅连接 http://127.0.0.1:18765/plan");
        return c;
    }
    private void ask(MinecraftClient mc, String task) {
        request(mc,task,true);
    }
    private void request(MinecraftClient mc,String task,boolean work) {
        if (pending != null || work && (active != null || !queue.isEmpty() || builder.hasPlan())) { say("当前任务未结束；可用 /scai stop 停止"); return; }
        if (mc.player == null || mc.world == null) return;
        if (task.isBlank() || task.length() > 300) { say("任务须为 1–300 字"); return; }
        try {
            config = readConfig();
            JsonArray history=new JsonArray();
            for(var e:conversation.context()) { JsonObject item=new JsonObject();item.addProperty("role",e.role());item.addProperty("content",e.text());history.add(item); }
            conversation.add("user",task);
            JsonObject state = new JsonObject();
            state.addProperty("position", mc.player.getBlockPos().toShortString());
            state.addProperty("dimension", mc.world.getRegistryKey().getValue().toString());
            state.addProperty("health", (int) mc.player.getHealth());
            state.addProperty("food", mc.player.getHungerManager().getFoodLevel());
            state.addProperty("yaw", Math.round(mc.player.getYaw()));
            state.addProperty("pitch", Math.round(mc.player.getPitch()));
            state.addProperty("held", net.minecraft.registry.Registries.ITEM.getId(mc.player.getMainHandStack().getItem()).toString());
            state.addProperty("interactionAllowed", config.has("allowWorldInteraction") && config.get("allowWorldInteraction").getAsBoolean() ? 1 : 0);
            state.addProperty("capabilities","blueprints-v1");
            state.add("environment",EnvironmentProbe.observe(mc));
            state.addProperty("execution",builder.hasPlan()?builder.status():"动作剩余="+(queue.size()+(active==null?0:1))+"；最近状态="+phase);
            if (mc.crosshairTarget instanceof BlockHitResult hit && hit.getType() == HitResult.Type.BLOCK)
                state.addProperty("target", net.minecraft.registry.Registries.BLOCK.getId(mc.world.getBlockState(hit.getBlockPos()).getBlock()).toString());
            JsonObject body = new JsonObject(); body.addProperty("task", task); body.add("state", state);
            body.addProperty("mode",work?"work":"chat"); body.add("history",history);
            String endpoint = config.get("endpoint").getAsString(), token = config.get("token").getAsString();
            int ticket = ++generation; world = mc.world;
            var startPosition = mc.player.getPos();
            float startYaw = mc.player.getYaw(), startPitch = mc.player.getPitch();
            int startSlot = mc.player.getInventory().selectedSlot;
            // Bounded stream read; never parse an unbounded network response on the render thread.
            pending = CompletableFuture.supplyAsync(() -> {
                HttpURLConnection c = null;
                try {
                    if (ticket != generation) throw new CancellationException();
                    c = (HttpURLConnection) URI.create(endpoint).toURL().openConnection(Proxy.NO_PROXY);
                    connection = c;
                    c.setConnectTimeout(3000); c.setReadTimeout(50000); c.setInstanceFollowRedirects(false);
                    c.setRequestMethod("POST"); c.setDoOutput(true);
                    c.setRequestProperty("Content-Type", "application/json"); c.setRequestProperty("Authorization", "Bearer " + token);
                    byte[] requestBytes = body.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
                    c.setFixedLengthStreamingMode(requestBytes.length);
                    if (ticket != generation) throw new CancellationException();
                    try (var output = c.getOutputStream()) { output.write(requestBytes); }
                    if (c.getResponseCode() != 200) throw new IllegalStateException("桥接服务返回 " + c.getResponseCode());
                    try (var stream = c.getInputStream()) {
                        byte[] bytes = stream.readNBytes(32769);
                        if (bytes.length > 32768) throw new IllegalArgumentException("桥接响应过大");
                        return JsonParser.parseString(new String(bytes, java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject();
                    }
                } catch (Exception e) { throw new CompletionException(e); }
                finally { if (c != null) c.disconnect(); if (connection == c) connection = null; }
            }, network).whenComplete((response, error) -> mc.execute(() -> {
                if (ticket != generation) return;
                pending = null;
                if (mc.world != world) { stop(mc); return; }
                if (error != null) { say("请求失败，请检查桥接服务（不会自动重试扣费）"); return; }
                if (work && (mc.player == null || mc.player.getPos().squaredDistanceTo(startPosition) > 1 ||
                    Math.abs(net.minecraft.util.math.MathHelper.wrapDegrees(mc.player.getYaw() - startYaw)) > 15 ||
                    Math.abs(mc.player.getPitch() - startPitch) > 15 || mc.player.getInventory().selectedSlot != startSlot)) {
                    say("玩家状态已改变，旧计划已丢弃；需要时请重新提问"); return;
                }
                try {
                    var actions = response.getAsJsonArray("actions");
                    if (actions == null || actions.size() > 8) throw new IllegalArgumentException();
                    if(!work && !actions.isEmpty())throw new IllegalArgumentException("对话不能执行动作");
                    // Validate the whole plan before queuing anything, including a tampered local response.
                    for (var a : actions) validate(a.getAsJsonObject());
                    JsonArray plan=response.has("plan")?response.getAsJsonArray("plan"):new JsonArray();
                    if(plan.size()>4)throw new IllegalArgumentException();
                    for(var step:plan)if(!step.isJsonPrimitive() || !step.getAsJsonPrimitive().isString() || step.getAsString().length()>60)throw new IllegalArgumentException();
                    String message=response.has("message")?response.get("message").getAsString():"已接收计划";
                    if(message.length()>200)throw new IllegalArgumentException();
                    conversation.add("assistant",message);
                    for(var step:plan)conversation.add("plan",step.getAsString());
                    if(!work){phase="对话完成";return;}
                    if (actions.size()>0 && actions.get(0).getAsJsonObject().get("type").getAsString().equals("build")) {
                        if (actions.size()!=1) throw new IllegalArgumentException();
                        var a=actions.get(0).getAsJsonObject();
                        builder.preview(mc,a.get("blueprint").getAsString(),a.get("material").getAsString()); return;
                    }
                    for(var a:actions) if(a.getAsJsonObject().get("type").getAsString().equals("build")) throw new IllegalArgumentException();
                    for (var a : actions) queue.add(a.getAsJsonObject());
                    say(message+(actions.isEmpty()?"":mc.currentScreen instanceof AiChatScreen?"；动作已排队，返回游戏后执行":"；开始执行 "+actions.size()+" 个动作"));
                } catch (Exception e) { if(work)queue.clear(); say("计划格式不合法，已拒绝执行"); }
            }));
            say(work?"正在生成任务计划…":"正在等待 AI 对话回复…");
        } catch (Exception e) { say(e.getMessage()); }
    }
    private void validate(JsonObject action) {
        String type = action.get("type").getAsString();
        if (!Set.of("wait", "look", "move", "select", "break", "use", "build").contains(type)) throw new IllegalArgumentException();
        if(type.equals("build") && (!Blueprints.NAMES.contains(action.get("blueprint").getAsString()) || !Blueprints.MATERIALS.contains(action.get("material").getAsString()))) throw new IllegalArgumentException();
        int ticks = action.has("ticks") ? action.get("ticks").getAsInt() : 1;
        if (ticks < 1 || ticks > 100) throw new IllegalArgumentException();
        if (type.equals("look")) {
            double yaw = action.get("yaw").getAsDouble(), pitch = action.get("pitch").getAsDouble();
            if (!Double.isFinite(yaw) || Math.abs(yaw) > 180 || !Double.isFinite(pitch) || Math.abs(pitch) > 90) throw new IllegalArgumentException();
        }
        if (type.equals("move") && !Set.of("forward", "back", "left", "right", "jump").contains(action.get("direction").getAsString())) throw new IllegalArgumentException();
        if (type.equals("select") && (action.get("slot").getAsInt() < 0 || action.get("slot").getAsInt() > 8)) throw new IllegalArgumentException();
    }
    private void tick(MinecraftClient mc) {
        if(dialogWorld!=mc.world){conversation.clear();phase="就绪";dialogWorld=mc.world;}
        if(openDialog){openDialog=false;if(mc.player!=null && mc.world!=null){release(mc);mc.setScreen(new AiChatScreen(this));}}
        if (mc.player == null || mc.world == null || (world != null && world != mc.world)) { if (pending != null || active != null || !queue.isEmpty() || builder.hasPlan()) stop(mc); return; }
        if(mc.currentScreen instanceof AiChatScreen){release(mc);return;}
        builder.tick(mc);
        if (active == null && queue.isEmpty()) return;
        if (mc.isPaused()) { release(mc); return; }
        if (mc.currentScreen != null || mc.player.getHealth() < 6 || mc.player.isDead()) { stop(mc); say("界面打开或生命值过低，任务停止"); return; }
        if (active == null) {
            release(mc); active = queue.poll();
            say("执行动作："+active.get("type").getAsString()+"，队列剩余 "+queue.size());
            actionTarget = null;
            remaining = active.has("ticks") ? active.get("ticks").getAsInt() : 1;
        }
        String type = active.get("type").getAsString();
        switch (type) {
            case "look" -> { mc.player.setYaw(active.get("yaw").getAsFloat()); mc.player.setPitch(active.get("pitch").getAsFloat()); }
            case "select" -> mc.player.getInventory().selectedSlot = active.get("slot").getAsInt();
            case "move" -> {
                if (mc.player.horizontalCollision || !mc.player.isOnGround() && !active.get("direction").getAsString().equals("jump")) { stop(mc); say("遇到障碍或离地，已停止"); return; }
                String direction = active.get("direction").getAsString();
                boolean safe=direction.equals("jump") ? EnvironmentProbe.loaded(mc,mc.player.getBlockPos().up(3)) && !EnvironmentProbe.hazardous(mc,mc.player.getBlockPos().down()) && mc.world.isSpaceEmpty(mc.player,mc.player.getBoundingBox().offset(0,1.25,0)) : EnvironmentProbe.safeStep(mc,EnvironmentProbe.step(mc,direction));
                if(!safe) { stop(mc); say("前方存在危险、悬空、障碍或未知区块，已停止"); return; }
                var key = switch (direction) { case "back" -> mc.options.backKey; case "left" -> mc.options.leftKey; case "right" -> mc.options.rightKey; case "jump" -> mc.options.jumpKey; default -> mc.options.forwardKey; };
                key.setPressed(true);
            }
            case "break", "use" -> {
                if (!config.has("allowWorldInteraction") || !config.get("allowWorldInteraction").getAsBoolean()) { stop(mc); say("方块交互未启用；在本地配置设 allowWorldInteraction=true 后使用"); return; }
                if (mc.interactionManager == null || !(mc.crosshairTarget instanceof BlockHitResult hit) || hit.getType() != HitResult.Type.BLOCK || mc.player.getEyePos().squaredDistanceTo(hit.getPos()) > 20.25) { stop(mc); say("没有可交互的近距离方块"); return; }
                if (type.equals("break")) {
                    if (actionTarget == null) actionTarget = hit.getBlockPos().toImmutable();
                    if (!actionTarget.equals(hit.getBlockPos())) { stop(mc); say("挖掘目标已改变，已停止"); return; }
                    mc.interactionManager.updateBlockBreakingProgress(hit.getBlockPos(), hit.getSide()); mc.player.swingHand(Hand.MAIN_HAND);
                }
                else { mc.interactionManager.interactBlock(mc.player, Hand.MAIN_HAND, hit); remaining = 1; }
            }
            default -> { }
        }
        if (--remaining <= 0) { active = null; release(mc); if (queue.isEmpty()) say("动作执行完毕；需要下一步时再输入 /scai ask"); }
    }
}
