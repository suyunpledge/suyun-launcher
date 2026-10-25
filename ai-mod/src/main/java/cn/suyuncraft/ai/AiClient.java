package cn.suyuncraft.ai;

import com.google.gson.*;
import com.mojang.brigadier.arguments.StringArgumentType;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.text.Text;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.hit.EntityHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.entity.mob.HostileEntity;
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
        if(ai.config!=null && ai.config.has("showWorkHud") && !ai.config.get("showWorkHud").getAsBoolean())return;
        if(mc.player==null || mc.options.hudHidden || mc.currentScreen instanceof AiChatScreen || ai.pending==null && ai.active==null && ai.queue.isEmpty() && !ai.builder.hasPlan() && !ai.gotoActive)return;
        int w=Math.max(20,Math.min(360,mc.getWindow().getScaledWidth()-16));
        int x=Math.min(ai.hudX,Math.max(0,mc.getWindow().getScaledWidth()-w-8));
        int y=Math.min(ai.hudY,Math.max(0,mc.getWindow().getScaledHeight()-28));
        ctx.fill(x,y,x+w+8,y+28,0xB0101824);
        ctx.drawTextWithShadow(mc.textRenderer,mc.textRenderer.trimToWidth("Suyun AI · "+(ai.pending!=null?"正在分析/等待接口":ai.phase),w),x+4,y+3,0xA8E8B8);
        String detail=ai.builder.hasPlan()?ai.builder.status():ai.gotoActive?"步行 → "+ai.gotoX+","+ai.gotoZ+" · 剩余 "+ai.gotoTicks+" tick":"剩余动作 "+(ai.queue.size()+(ai.active==null?0:1))+" · /scai 打开对话";
        ctx.drawTextWithShadow(mc.textRenderer,mc.textRenderer.trimToWidth(detail,w),x+4,y+16,0xFFFFFF);
    }
    private final ExecutorService network = new ThreadPoolExecutor(1,1,0L,TimeUnit.MILLISECONDS,new ArrayBlockingQueue<>(1),r -> {
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
    private final InputLease<net.minecraft.client.option.KeyBinding> input = new InputLease<>();
    private boolean miningOwned;
    private int hudX=6, hudY=6;
    private boolean aiming; private float aimYaw, aimPitch; private int aimLife;
    private boolean gotoActive; private int gotoX, gotoZ, gotoTicks, gotoBlocked;
    private int airGrace, collisionTicks;
    private net.minecraft.util.math.Vec3d lastSample;
    private HostileEntity attackTargetEntity;

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
            .then(literal("goto").then(argument("target",StringArgumentType.greedyString()).executes(c -> {
                beginGoto(MinecraftClient.getInstance(),StringArgumentType.getString(c,"target")); return 1;
            })))
            .then(literal("fight").executes(c -> { beginFight(MinecraftClient.getInstance(),"100"); return 1; })
                .then(argument("ticks",StringArgumentType.greedyString()).executes(c -> {
                    beginFight(MinecraftClient.getInstance(),StringArgumentType.getString(c,"ticks")); return 1;
                })))
            .then(literal("confirm").executes(c -> { beginBuild(); return 1; }))
            .then(literal("resume").executes(c -> { beginBuild(); return 1; }))
            .then(literal("status").executes(c -> { say("请求中=" + (pending != null) + "，剩余动作=" + (queue.size() + (active == null ? 0 : 1))+"；"+builder.status()); return 1; }))));
        ClientTickEvents.END_CLIENT_TICK.register(this::tick);
        ClientLifecycleEvents.CLIENT_STOPPING.register(mc -> { stop(mc); network.shutdownNow(); });
    }
    private void say(String message) {
        if(message==null)message="状态更新";
        phase=message.substring(0,Math.min(120,message.length()));
        conversation.add("status",message);
        var player = MinecraftClient.getInstance().player;
        if (player != null) player.sendMessage(Text.literal("[Suyun AI] " + message), false);
    }
    List<ConversationLog.Entry> dialogLines() { return conversation.lines(); }
    String dialogStatus() { return (pending!=null?"正在分析与规划（等待接口）":phase)+" | "+(builder.hasPlan()?builder.status():gotoActive?"步行 → "+gotoX+","+gotoZ+" 剩余 "+gotoTicks+" tick":"动作剩余="+(queue.size()+(active==null?0:1))); }
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
        input.release(key -> key.setPressed(false));
        if (miningOwned && mc.interactionManager != null) mc.interactionManager.cancelBlockBreaking();
        miningOwned=false;
    }
    private void stop(MinecraftClient mc) {
        builder.stop();
        generation++;
        if (pending != null) pending.cancel(true);
        if (connection != null) connection.disconnect();
        pending = null; queue.clear(); active = null; remaining = 0; release(mc);
        gotoActive=false; gotoBlocked=0; aiming=false; airGrace=0; collisionTicks=0; attackTargetEntity=null;
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
    /** Local zero-API walking: smooth steering, auto step-up, bounded stall detection. */
    private void beginGoto(MinecraftClient mc,String arguments) {
        if (mc.player==null || mc.world==null) return;
        if (pending!=null || active!=null || !queue.isEmpty() || builder.hasPlan() || gotoActive) { say("先 /scai stop 结束当前任务"); return; }
        try {
            String[] args=arguments.trim().split("\\s+");
            if(args.length<2 || args.length>3) throw new IllegalArgumentException("用法：/scai goto <x> <z> [步数上限1-400]");
            int x=Integer.parseInt(args[0]), z=Integer.parseInt(args[1]);
            int steps=args.length==3?Integer.parseInt(args[2]):100;
            if(Math.abs(x)>30_000_000 || Math.abs(z)>30_000_000) throw new IllegalArgumentException("坐标超出世界范围");
            if(steps<1 || steps>400) throw new IllegalArgumentException("步数上限须在 1–400");
            gotoX=x; gotoZ=z; gotoTicks=steps; gotoActive=true; gotoBlocked=0; collisionTicks=0;
            lastSample=mc.player.getPos(); world=mc.world;
            double dist=Math.sqrt(Math.pow(x-mc.player.getX(),2)+Math.pow(z-mc.player.getZ(),2));
            say(String.format("开始步行 → %d,%d（距 %.0f 格，最多 %d tick）；不调用 API，遇到危险自动停止", x,z,dist,steps));
        } catch(NumberFormatException e) { say("坐标和步数必须是整数"); }
    }
    /** Local zero-API combat: lock the nearest hostile, close in and swing on cooldown. */
    private void beginFight(MinecraftClient mc,String argument) {
        if (mc.player==null || mc.world==null) return;
        if (pending!=null || active!=null || !queue.isEmpty() || builder.hasPlan() || gotoActive) { say("先 /scai stop 结束当前任务"); return; }
        try {
            int ticks=Integer.parseInt(argument.trim());
            if(ticks<1 || ticks>200) throw new IllegalArgumentException("战斗时长须在 1–200 tick");
            config=readConfig();
            if(!config.has("allowCombat") || !config.get("allowCombat").getAsBoolean()) { say("战斗未启用；在本地配置设 allowCombat=true 后使用"); return; }
            if(EnvironmentProbe.nearestHostile(mc,8)==null) { say("附近 8 格内没有敌对生物"); return; }
            JsonObject action=new JsonObject(); action.addProperty("type","attack");
            action.addProperty("ticks",ticks); action.addProperty("target","nearest"); action.addProperty("approach",true);
            queue.add(action); world=mc.world;
            say("开始本地战斗，不调用 API；生命值偏低或目标逃离会自动停止");
        } catch(NumberFormatException e) { say("战斗时长必须是整数"); }
        catch(Exception e) { say(e.getMessage()); }
    }
    /** Interpolated head rotation: never snaps, keeps turning across action boundaries, bounded lifetime. */
    private void setAim(float yaw,float pitch) { aimYaw=Motion.wrapDegrees(yaw); aimPitch=net.minecraft.util.math.MathHelper.clamp(pitch,-90f,90f); aiming=true; aimLife=100; }
    private void steer(MinecraftClient mc) {
        if(!aiming) return;
        var p=mc.player;
        p.setYaw(Motion.turnTowards(p.getYaw(),aimYaw,Motion.YAW_SPEED));
        p.setPitch(net.minecraft.util.math.MathHelper.clamp(Motion.turnTowards(p.getPitch(),aimPitch,Motion.PITCH_SPEED),-90f,90f));
        if(Motion.arrived(p.getYaw(),p.getPitch(),aimYaw,aimPitch) || --aimLife<=0) aiming=false;
    }
    /** A 1-block step is climbable by jumping: free body space above the obstacle and solid safe landing. */
    private boolean canStepUp(MinecraftClient mc,String direction) {
        var step=EnvironmentProbe.step(mc,direction);
        if(!mc.player.isOnGround()) return false;
        BlockPos landing=new BlockPos((int)Math.floor(mc.player.getX()+step.x),mc.player.getBlockY()+1,(int)Math.floor(mc.player.getZ()+step.z));
        BlockPos head=mc.player.getBlockPos();
        return EnvironmentProbe.loaded(mc,landing) && EnvironmentProbe.loaded(mc,head.up(3)) &&
            !EnvironmentProbe.hazardous(mc,landing) && !EnvironmentProbe.hazardous(mc,landing.up()) &&
            !EnvironmentProbe.hazardous(mc,head.down()) &&
            !mc.world.getBlockState(landing.down()).getCollisionShape(mc.world,landing.down()).isEmpty() &&
            mc.world.isSpaceEmpty(mc.player,mc.player.getBoundingBox().offset(step.x,1.05,step.z));
    }
    private JsonObject readConfig() throws Exception {
        Path path = FabricLoader.getInstance().getConfigDir().resolve("suyuncraft-ai.json");
        if (!Files.exists(path)) throw new IllegalStateException("请先从启动器安装 AI 模组并启动本地桥接服务");
        byte[] bytes;
        try(var stream=Files.newInputStream(path)) { bytes=stream.readNBytes(65537); }
        if(bytes.length>65536)throw new IllegalArgumentException("AI 配置超过 64 KB");
        JsonObject c = JsonParser.parseString(new String(bytes,java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject();
        if(c.has("showWorkHud") && (!c.get("showWorkHud").isJsonPrimitive() || !c.getAsJsonPrimitive("showWorkHud").isBoolean()))throw new IllegalArgumentException("showWorkHud 必须为布尔值");
        for(String flag:new String[]{"allowCommands","allowCombat"}) if(c.has(flag) && (!c.get(flag).isJsonPrimitive() || !c.getAsJsonPrimitive(flag).isBoolean())) throw new IllegalArgumentException(flag+" 必须为布尔值");
        int x=c.has("hudX")?c.get("hudX").getAsInt():6, y=c.has("hudY")?c.get("hudY").getAsInt():6;
        if(x<0 || y<0 || x>8192 || y>8192)throw new IllegalArgumentException("HUD 坐标须在 0–8192 之间");
        URI endpoint = URI.create(c.get("endpoint").getAsString());
        if (!"http".equals(endpoint.getScheme()) || !"127.0.0.1".equals(endpoint.getHost()) || endpoint.getPort() != 18765 || !"/plan".equals(endpoint.getPath()) || endpoint.getUserInfo()!=null || endpoint.getQuery()!=null || endpoint.getFragment()!=null)
            throw new IllegalArgumentException("模组仅连接 http://127.0.0.1:18765/plan");
        hudX=x;hudY=y;
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
            state.addProperty("commandsAllowed", config.has("allowCommands") && config.get("allowCommands").getAsBoolean() ? 1 : 0);
            state.addProperty("combatAllowed", config.has("allowCombat") && config.get("allowCombat").getAsBoolean() ? 1 : 0);
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
        } catch (RejectedExecutionException e) { say("上一次请求仍在结束，请稍后再试；没有发送新请求"); }
        catch (Exception e) { say(e.getMessage()); }
    }
    private void validate(JsonObject action) {
        String type = action.get("type").getAsString();
        if (!Set.of("wait", "look", "lookat", "move", "select", "break", "use", "build", "command", "attack").contains(type)) throw new IllegalArgumentException();
        if(type.equals("build") && (!Blueprints.NAMES.contains(action.get("blueprint").getAsString()) || !Blueprints.MATERIALS.contains(action.get("material").getAsString()))) throw new IllegalArgumentException();
        if(type.equals("command") && !Commands.allowed(action.get("command").getAsString())) throw new IllegalArgumentException();
        int ticks = action.has("ticks") ? action.get("ticks").getAsInt() : 1;
        if (ticks < 1 || ticks > 100) throw new IllegalArgumentException();
        if (type.equals("look")) {
            double yaw = action.get("yaw").getAsDouble(), pitch = action.get("pitch").getAsDouble();
            if (!Double.isFinite(yaw) || Math.abs(yaw) > 180 || !Double.isFinite(pitch) || Math.abs(pitch) > 90) throw new IllegalArgumentException();
        }
        if (type.equals("lookat")) {
            for (String axis : new String[]{"x", "y", "z"}) {
                double v = action.get(axis).getAsDouble();
                if (!Double.isFinite(v) || Math.abs(v) > ("y".equals(axis) ? 2048 : 30_000_000)) throw new IllegalArgumentException();
            }
        }
        if (type.equals("move")) {
            if (!Set.of("forward", "back", "left", "right", "jump").contains(action.get("direction").getAsString())) throw new IllegalArgumentException();
            if (action.has("yaw")) {
                double yaw = action.get("yaw").getAsDouble();
                if (!Double.isFinite(yaw) || Math.abs(yaw) > 180) throw new IllegalArgumentException();
            }
            if (action.has("sprint") && (!action.get("sprint").isJsonPrimitive() || !action.getAsJsonPrimitive("sprint").isBoolean())) throw new IllegalArgumentException();
        }
        if (type.equals("select") && (action.get("slot").getAsInt() < 0 || action.get("slot").getAsInt() > 8)) throw new IllegalArgumentException();
        if (type.equals("attack")) {
            if (action.has("target") && !"nearest".equals(action.get("target").getAsString())) throw new IllegalArgumentException();
            if (action.has("approach") && (!action.get("approach").isJsonPrimitive() || !action.getAsJsonPrimitive("approach").isBoolean())) throw new IllegalArgumentException();
        }
    }
    private void tick(MinecraftClient mc) {
        if(dialogWorld!=mc.world){conversation.clear();phase="就绪";dialogWorld=mc.world;}
        if(openDialog){openDialog=false;if(mc.player!=null && mc.world!=null){release(mc);mc.setScreen(new AiChatScreen(this));}}
        if (mc.player == null || mc.world == null || (world != null && world != mc.world)) { if (pending != null || active != null || !queue.isEmpty() || builder.hasPlan() || gotoActive) stop(mc); return; }
        if(mc.currentScreen instanceof AiChatScreen){release(mc);return;}
        builder.tick(mc);
        if (gotoActive) { tickGoto(mc); return; }
        if (active == null && queue.isEmpty()) { steer(mc); return; }
        if (mc.isPaused()) { release(mc); return; }
        if (mc.currentScreen != null || mc.player.getHealth() < 6 || mc.player.isDead()) { stop(mc); say("界面打开或生命值过低，任务停止"); return; }
        if (active == null) {
            release(mc); active = queue.poll();
            say("执行动作："+active.get("type").getAsString()+"，队列剩余 "+queue.size());
            actionTarget = null; attackTargetEntity = null;
            remaining = Math.max(active.get("type").getAsString().equals("look") || active.get("type").getAsString().equals("lookat") ? 2 : 1,
                active.has("ticks") ? active.get("ticks").getAsInt() : 1);
            if (active.get("type").getAsString().equals("move") && active.has("yaw")) setAim(active.get("yaw").getAsFloat(), mc.player.getPitch());
        }
        steer(mc);
        String type = active.get("type").getAsString();
        switch (type) {
            case "look" -> setAim(active.get("yaw").getAsFloat(), active.get("pitch").getAsFloat());
            case "lookat" -> {
                var eye=mc.player.getEyePos();
                var center=net.minecraft.util.math.Vec3d.ofCenter(new BlockPos((int)Math.floor(active.get("x").getAsDouble()),
                    (int)Math.floor(active.get("y").getAsDouble()),(int)Math.floor(active.get("z").getAsDouble())));
                if(eye.squaredDistanceTo(center)>24*24) { stop(mc); say("lookat 目标超过 24 格，已停止"); return; }
                setAim(Motion.aimYaw(center.x-eye.x,center.z-eye.z), Motion.aimPitch(center.x-eye.x,center.y-eye.y,center.z-eye.z));
            }
            case "select" -> mc.player.getInventory().selectedSlot = active.get("slot").getAsInt();
            case "move" -> {
                String direction = active.get("direction").getAsString();
                if (direction.equals("jump")) {
                    if (!EnvironmentProbe.loaded(mc,mc.player.getBlockPos().up(3)) || EnvironmentProbe.hazardous(mc,mc.player.getBlockPos().down()) || !mc.world.isSpaceEmpty(mc.player,mc.player.getBoundingBox().offset(0,1.25,0))) { stop(mc); say("跳跃空间不足或脚下危险，已停止"); return; }
                    airGrace=10; collisionTicks=0;
                    input.hold(mc.options.jumpKey, k -> k.setPressed(true));
                } else {
                    if (!mc.player.isOnGround()) {
                        if (airGrace <= 0 || !EnvironmentProbe.descentSafe(mc)) { stop(mc); say("离地且无法确认下落安全，已停止"); return; }
                        airGrace--;
                    } else airGrace=Math.max(airGrace-1,0);
                    boolean clear=EnvironmentProbe.safeStep(mc,EnvironmentProbe.step(mc,direction));
                    if(!clear && !canStepUp(mc,direction)) { stop(mc); say("前方存在危险、悬空、障碍或未知区块，已停止"); return; }
                    var key = switch (direction) { case "back" -> mc.options.backKey; case "left" -> mc.options.leftKey; case "right" -> mc.options.rightKey; case "jump" -> mc.options.jumpKey; default -> mc.options.forwardKey; };
                    input.hold(key, k -> k.setPressed(true));
                    if(!clear) input.hold(mc.options.jumpKey, k -> k.setPressed(true));
                    if(active.has("sprint") && active.get("sprint").getAsBoolean() && direction.equals("forward") && !mc.player.isSprinting())
                        input.hold(mc.options.sprintKey, k -> k.setPressed(true));
                    if (mc.player.horizontalCollision && mc.player.isOnGround() && ++collisionTicks > 8) { stop(mc); say("持续受阻，已停止；可尝试跳跃或调整方向"); return; }
                    if (!mc.player.horizontalCollision) collisionTicks=0;
                }
            }
            case "break", "use" -> {
                if (!config.has("allowWorldInteraction") || !config.get("allowWorldInteraction").getAsBoolean()) { stop(mc); say("方块交互未启用；在本地配置设 allowWorldInteraction=true 后使用"); return; }
                if (mc.interactionManager == null || !(mc.crosshairTarget instanceof BlockHitResult hit) || hit.getType() != HitResult.Type.BLOCK || mc.player.getEyePos().squaredDistanceTo(hit.getPos()) > 20.25) { stop(mc); say("没有可交互的近距离方块"); return; }
                if (type.equals("break")) {
                    if (actionTarget == null) actionTarget = hit.getBlockPos().toImmutable();
                    if (!actionTarget.equals(hit.getBlockPos())) { stop(mc); say("挖掘目标已改变，已停止"); return; }
                    miningOwned=true;
                    mc.interactionManager.updateBlockBreakingProgress(hit.getBlockPos(), hit.getSide()); mc.player.swingHand(Hand.MAIN_HAND);
                }
                else { mc.interactionManager.interactBlock(mc.player, Hand.MAIN_HAND, hit); remaining = 1; }
            }
            case "command" -> {
                if (!config.has("allowCommands") || !config.get("allowCommands").getAsBoolean()) { stop(mc); say("指令未启用；在本地配置设 allowCommands=true 后使用"); return; }
                String command=active.get("command").getAsString().trim();
                if (!Commands.allowed(command) || mc.player.networkHandler == null) { stop(mc); say("指令不在白名单内，已拒绝"); return; }
                mc.player.networkHandler.sendCommand(command);
                say("已发送指令：/"+command);
                remaining = 1;
            }
            case "attack" -> {
                if (!config.has("allowCombat") || !config.get("allowCombat").getAsBoolean()) { stop(mc); say("战斗未启用；在本地配置设 allowCombat=true 后使用"); return; }
                boolean nearest=active.has("target") && "nearest".equals(active.get("target").getAsString());
                HostileEntity target;
                if (nearest) {
                    if (attackTargetEntity == null || !attackTargetEntity.isAlive()) {
                        boolean had=attackTargetEntity != null;
                        attackTargetEntity = EnvironmentProbe.nearestHostile(mc, 8);
                        if (attackTargetEntity == null) { stop(mc); say(had?"目标已被击败，附近没有其他敌对生物":"附近没有可攻击的敌对生物"); return; }
                        if (had) say("目标已被击败，转向下一个目标");
                    }
                    target = attackTargetEntity;
                } else {
                    if (!(mc.crosshairTarget instanceof EntityHitResult hit) || !(hit.getEntity() instanceof HostileEntity hostile)) { stop(mc); say("准星处没有敌对生物；可先用 lookat 瞄准"); return; }
                    target = hostile;
                }
                if (mc.player.getHealth() < 10) { stop(mc); say("生命值偏低，已停止战斗"); return; }
                if (target.squaredDistanceTo(mc.player) > 12*12) { stop(mc); say("目标已远离，停止攻击"); return; }
                var eye=mc.player.getEyePos(); var center=target.getBoundingBox().getCenter();
                setAim(Motion.aimYaw(center.x-eye.x,center.z-eye.z), Motion.aimPitch(center.x-eye.x,center.y-eye.y,center.z-eye.z));
                if (active.has("approach") && active.get("approach").getAsBoolean() && target.squaredDistanceTo(mc.player) > 3.5*3.5) {
                    boolean clear=EnvironmentProbe.safeStep(mc,EnvironmentProbe.step(mc,"forward"));
                    if (clear || canStepUp(mc,"forward")) {
                        input.hold(mc.options.forwardKey, k -> k.setPressed(true));
                        if(!clear) input.hold(mc.options.jumpKey, k -> k.setPressed(true));
                    }
                }
                if (mc.crosshairTarget instanceof EntityHitResult aimed && aimed.getEntity() == target && mc.player.getAttackCooldownProgress(0.0f) >= 1.0f) {
                    if (mc.interactionManager != null) mc.interactionManager.attackEntity(mc.player, target);
                    mc.player.swingHand(Hand.MAIN_HAND);
                }
            }
            default -> { }
        }
        if (--remaining <= 0) { active = null; release(mc); if (queue.isEmpty()) say("动作执行完毕；需要下一步时再输入 /scai ask"); }
    }
    /** Walk toward gotoX/gotoZ: turn smoothly, hold forward while roughly facing, hop 1-block steps, abort on stall or danger. */
    private void tickGoto(MinecraftClient mc) {
        if (mc.isPaused()) { release(mc); return; }
        if (mc.currentScreen != null || mc.player.getHealth() < 6 || mc.player.isDead()) { stop(mc); say("界面打开或生命值过低，步行已停止"); return; }
        double dx=gotoX-mc.player.getX(), dz=gotoZ-mc.player.getZ(), dist=Math.sqrt(dx*dx+dz*dz);
        if (dist < 1.5) { stop(mc); say(String.format("已到达 %d,%d 附近",gotoX,gotoZ)); return; }
        if (--gotoTicks <= 0) { stop(mc); say(String.format("步数用尽，距 %d,%d 还有约 %.0f 格；可用 /scai goto 继续",gotoX,gotoZ,dist)); return; }
        float wantYaw=Motion.aimYaw(dx,dz);
        setAim(wantYaw,0); steer(mc);
        boolean clear=EnvironmentProbe.safeStep(mc,EnvironmentProbe.step(mc,"forward"));
        if(!clear && !canStepUp(mc,"forward")) { stop(mc); say(String.format("前方危险或无法通行，步行已停止（距目标约 %.0f 格）",dist)); return; }
        input.release(key -> key.setPressed(false));
        if (Math.abs(Motion.wrapDegrees(wantYaw-mc.player.getYaw())) < 75) {
            input.hold(mc.options.forwardKey, k -> k.setPressed(true));
            if(!clear) input.hold(mc.options.jumpKey, k -> k.setPressed(true));
        }
        if (mc.player.horizontalCollision && mc.player.isOnGround()) gotoBlocked++; else if (mc.player.isOnGround()) gotoBlocked=Math.max(gotoBlocked-2,0);
        if (gotoBlocked > 24) { stop(mc); say("步行持续受阻，已停止；请手动通过后再次 /scai goto"); return; }
        if (gotoTicks % 30 == 0) {
            if (lastSample != null && mc.player.isOnGround() && mc.player.getPos().squaredDistanceTo(lastSample) < 1.0) { stop(mc); say("步行似乎卡住，已停止；请手动脱困后再次 /scai goto"); return; }
            lastSample=mc.player.getPos();
        }
        if (gotoTicks % 40 == 0) phase=String.format("步行中，距目标约 %.0f 格",dist);
    }
}
