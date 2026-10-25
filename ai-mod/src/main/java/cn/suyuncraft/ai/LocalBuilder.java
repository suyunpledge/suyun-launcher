package cn.suyuncraft.ai;

import net.minecraft.block.Block;
import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.item.BlockItem;
import net.minecraft.registry.Registries;
import net.minecraft.util.Hand;
import net.minecraft.util.Identifier;
import net.minecraft.util.hit.*;
import net.minecraft.util.math.*;
import net.minecraft.world.RaycastContext;
import java.util.*;
import java.util.function.Consumer;

/** One local blueprint at a fixed anchor, no teleporting, commands or inventory injection. */
final class LocalBuilder {
    private final Consumer<String> say;
    private final List<BlockPos> remaining=new ArrayList<>();
    private Object world;
    private Block material;
    private boolean running;
    private BlockPos pending;
    private final PlacementReceipt receipt=new PlacementReceipt();
    private int delay, confirmed, existing, total;
    private long started;
    private BlockPos aimPos; private float aimYaw, aimPitch; private int aimWait;
    LocalBuilder(Consumer<String> say) { this.say=say; }
    private void say(String text) { say.accept(text); }
    boolean hasPlan() { return !remaining.isEmpty(); }
    void stop() { remaining.clear(); pending=null; running=false; world=null; aimPos=null; }
    String status() { return "建造="+(running?"执行":"预览/暂停")+"，服务器确认="+confirmed+"，已有="+existing+"，剩余="+remaining.size()+"/"+total; }
    void preview(MinecraftClient mc,String name,String id) {
        if (!Blueprints.MATERIALS.contains(id)) throw new IllegalArgumentException("材料仅支持圆石、石头、泥土、橡木板、玻璃、砖块、砂岩");
        var cells=Blueprints.cells(name);
        stop(); confirmed=0; existing=0; total=cells.size();
        material=Registries.BLOCK.get(Identifier.of(id)); world=mc.world;
        int rotation=switch(mc.player.getHorizontalFacing()) { case WEST->1; case NORTH->2; case EAST->3; default->0; };
        BlockPos anchor=mc.player.getBlockPos();
        for (var cell:cells) {
            var c=cell.rotate(rotation); BlockPos p=anchor.add(c.x(),c.y(),c.z());
            if (!EnvironmentProbe.loaded(mc,p) || !mc.world.getWorldBorder().contains(p)) { stop(); throw new IllegalArgumentException("蓝图超出已加载区域、世界边界或高度范围"); }
            var s=mc.world.getBlockState(p);
            if (s.isOf(material)) { existing++; continue; }
            if (!s.isAir()) { stop(); throw new IllegalArgumentException("蓝图位置存在其他方块，拒绝覆盖："+p.toShortString()); }
            remaining.add(p);
        }
        say("蓝图 "+name+" 锚点 "+anchor.toShortString()+"，需要 "+remaining.size()+" 个 "+id+"，快捷栏有 "+count(mc)+"。用 /scai confirm 开始；/scai stop 取消。");
        if (remaining.isEmpty()) { say("所有蓝图位置已有相同方块，无需放置"); stop(); }
    }
    private int count(MinecraftClient mc) {
        int n=0; for(int i=0;i<9;i++) { var s=mc.player.getInventory().getStack(i); if (s.getItem() instanceof BlockItem b && b.getBlock()==material) n+=s.getCount(); } return n;
    }
    void begin(MinecraftClient mc,boolean allowed) {
        if (!hasPlan()) throw new IllegalArgumentException("没有蓝图；先 /scai build floor3");
        if (!allowed) throw new IllegalArgumentException("先在配置中启用 allowWorldInteraction=true");
        if (mc.world!=world) { stop(); throw new IllegalArgumentException("世界已改变，请重新预览"); }
        // The in-flight block may already have consumed one item while awaiting a server packet.
        int needed=remaining.size()-(pending==null?0:1);
        if (!mc.player.isCreative() && count(mc)<needed) throw new IllegalArgumentException("快捷栏材料不足；补足后 /scai resume");
        running=true; started=System.nanoTime(); receipt.resume(); delay=5;
        say("开始本地建造；不调用 API。超出触及范围时会暂停，移动到附近后 /scai resume。");
    }
    void serverUpdate(BlockPos pos,BlockState state) {
        if (pending!=null && pending.equals(pos)) receipt.observe(state.isOf(material));
    }
    private void pause(String reason) { running=false; say(reason+"；已暂停，/scai resume 继续，/scai stop 取消。"); }
    void tick(MinecraftClient mc) {
        if (!hasPlan()) return;
        if (mc.world!=world || mc.player==null) { stop(); return; }
        if (!running) return;
        if (mc.isPaused()) return;
        if (mc.interactionManager==null) { pause("交互控制器未就绪"); return; }
        if (mc.currentScreen!=null || mc.player.getHealth()<8 || mc.player.isDead() || mc.player.isOnFire()) { pause("界面打开、低血量或着火"); return; }
        if (System.nanoTime()-started>180_000_000_000L) { pause("单次执行达到三分钟"); return; }
        if (mc.world.getEntitiesByClass(net.minecraft.entity.mob.HostileEntity.class,mc.player.getBoundingBox().expand(6),e->e.isAlive()).size()>0) { pause("附近有敌对生物"); return; }
        if (pending!=null) {
            var result=receipt.tick();
            if (result==PlacementReceipt.Result.CONFIRMED || result==PlacementReceipt.Result.REJECTED) {
                if (result==PlacementReceipt.Result.REJECTED) { pending=null; pause("服务器未接受放置"); return; }
                remaining.remove(pending); pending=null; confirmed++; delay=5;
                if (remaining.isEmpty()) { say("蓝图完成：服务器确认 "+confirmed+" 块，原有 "+existing+" 块"); stop(); }
                return;
            }
            if (result==PlacementReceipt.Result.TIMEOUT) { pause("等待服务器方块回包超时，不会自动重复发送"); }
            return;
        }
        if (aimPos!=null) {
            var player=mc.player;
            player.setYaw(Motion.turnTowards(player.getYaw(),aimYaw,Motion.YAW_SPEED));
            player.setPitch(MathHelper.clamp(Motion.turnTowards(player.getPitch(),aimPitch,Motion.PITCH_SPEED),-90f,90f));
            if (!Motion.arrived(player.getYaw(),player.getPitch(),aimYaw,aimPitch) && --aimWait>0) return;
            var target=aimPos; aimPos=null;
            var hit=findSupport(mc,target);
            if (hit==null) { delay=2; return; }
            int slot=-1;
            for (int i=0;i<9;i++) { var s=mc.player.getInventory().getStack(i); if(!s.isEmpty() && s.getItem() instanceof BlockItem b && b.getBlock()==material) {slot=i;break;} }
            if (slot<0) { pause("快捷栏中没有可用材料"); return; }
            mc.player.getInventory().selectedSlot=slot;
            pending=target; receipt.reset();
            var result=mc.interactionManager.interactBlock(mc.player,Hand.MAIN_HAND,hit);
            if (!result.isAccepted()) { pending=null; pause("本地放置检查失败"); return; }
            mc.player.swingHand(Hand.MAIN_HAND);
            return;
        }
        if (--delay>0) return;
        for (var p:remaining) {
            if (!EnvironmentProbe.loaded(mc,p)) { pause("区块未加载"); return; }
            var state=mc.world.getBlockState(p);
            if (state.isOf(material)) { remaining.remove(p); existing++; if(remaining.isEmpty()){say("蓝图位置已满足；服务器确认放置 "+confirmed+" 块");stop();} return; }
            if (!state.isAir()) { pause("蓝图位置被占用，拒绝覆盖"); return; }
            if (EnvironmentProbe.hazardous(mc,p.down())) continue;
            if (!mc.world.getOtherEntities(null,new Box(p),e->e.isAlive() && !e.isSpectator()).isEmpty()) continue;
            BlockHitResult hit=findSupport(mc,p);
            if (hit==null) continue;
            int slot=-1;
            for (int i=0;i<9;i++) { var s=mc.player.getInventory().getStack(i); if(!s.isEmpty() && s.getItem() instanceof BlockItem b && b.getBlock()==material) {slot=i;break;} }
            if (slot<0) { pause("快捷栏中没有可用材料"); return; }
            Vec3d aim=hit.getPos().subtract(mc.player.getEyePos());
            aimPos=p; aimYaw=Motion.aimYaw(aim.x,aim.z); aimPitch=Motion.aimPitch(aim.x,aim.y,aim.z); aimWait=14;
            return;
        }
        pause("没有可见、可触及且有支撑的放置位置；请调整站位或手动补充支撑");
    }
    private BlockHitResult findSupport(MinecraftClient mc,BlockPos target) {
        for (Direction d:Direction.values()) {
            BlockPos support=target.offset(d); Direction face=d.getOpposite();
            if (!EnvironmentProbe.loaded(mc,support)) continue;
            var state=mc.world.getBlockState(support);
            if (!state.isFullCube(mc.world,support) || state.hasBlockEntity() || EnvironmentProbe.hazardous(mc,support)) continue;
            Vec3d point=Vec3d.ofCenter(support).add(face.getOffsetX()*0.5,face.getOffsetY()*0.5,face.getOffsetZ()*0.5);
            double reach=Math.min(4.5,mc.player.getBlockInteractionRange());
            if (mc.player.getEyePos().squaredDistanceTo(point)>reach*reach) continue;
            Vec3d end=point.add(-face.getOffsetX()*0.002,-face.getOffsetY()*0.002,-face.getOffsetZ()*0.002);
            var ray=mc.world.raycast(new RaycastContext(mc.player.getEyePos(),end,RaycastContext.ShapeType.COLLIDER,RaycastContext.FluidHandling.ANY,mc.player));
            if (ray.getType()==HitResult.Type.BLOCK && ray.getBlockPos().equals(support) && ray.getSide()==face)
                return new BlockHitResult(point,face,support,false);
        }
        return null;
    }
}
