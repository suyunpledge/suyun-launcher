package cn.suyuncraft.ai;

import com.google.gson.*;
import net.minecraft.block.Blocks;
import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.mob.HostileEntity;
import net.minecraft.registry.Registries;
import net.minecraft.util.math.*;

/** Bounded local observations. Unknown chunks are never interpreted as safe air. */
final class EnvironmentProbe {
    static boolean loaded(MinecraftClient mc, BlockPos p) {
        return !mc.world.isOutOfHeightLimit(p) && mc.world.isChunkLoaded(p.getX() >> 4, p.getZ() >> 4);
    }
    static boolean hazardous(MinecraftClient mc, BlockPos p) {
        if (!loaded(mc,p)) return true;
        var s=mc.world.getBlockState(p);
        return !s.getFluidState().isEmpty() || s.isOf(Blocks.FIRE) || s.isOf(Blocks.SOUL_FIRE) ||
            s.isOf(Blocks.MAGMA_BLOCK) || s.isOf(Blocks.CACTUS) || s.isOf(Blocks.SWEET_BERRY_BUSH) ||
            s.isOf(Blocks.POWDER_SNOW) || s.isOf(Blocks.CAMPFIRE) || s.isOf(Blocks.SOUL_CAMPFIRE);
    }
    static boolean safeStep(MinecraftClient mc, Vec3d step) {
        var box=mc.player.getBoundingBox().offset(step).expand(0.05,0,0.05);
        for (int x=MathHelper.floor(box.minX); x<=MathHelper.floor(box.maxX); x++)
            for (int z=MathHelper.floor(box.minZ); z<=MathHelper.floor(box.maxZ); z++) {
                BlockPos feet=new BlockPos(x,mc.player.getBlockY(),z);
                if (!loaded(mc,feet.up()) || hazardous(mc,feet) || hazardous(mc,feet.up()) || hazardous(mc,feet.down())) return false;
                if (mc.world.getBlockState(feet.down()).getCollisionShape(mc.world,feet.down()).isEmpty()) return false;
            }
        return mc.world.isSpaceEmpty(mc.player,box);
    }
    static Vec3d step(MinecraftClient mc, String direction) {
        double yaw=Math.toRadians(mc.player.getYaw());
        Vec3d forward=new Vec3d(-Math.sin(yaw),0,Math.cos(yaw));
        return switch(direction) {
            case "back" -> forward.multiply(-0.65); case "left" -> new Vec3d(forward.z,0,-forward.x).multiply(0.65);
            case "right" -> new Vec3d(-forward.z,0,forward.x).multiply(0.65); default -> forward.multiply(0.65);
        };
    }
    static JsonObject observe(MinecraftClient mc) {
        JsonObject env=new JsonObject();
        int unknown=0,hazards=0,solid=0;
        BlockPos center=mc.player.getBlockPos();
        for (int x=-3;x<=3;x++) for (int z=-3;z<=3;z++) for (int y=-1;y<=2;y++) {
            BlockPos p=center.add(x,y,z);
            if (!loaded(mc,p)) { unknown++; continue; }
            if (hazardous(mc,p)) hazards++;
            if (mc.world.getBlockState(p).isFullCube(mc.world,p)) solid++;
        }
        env.addProperty("scanRadius",3); env.addProperty("unknownCells",unknown);
        env.addProperty("hazardCells",hazards); env.addProperty("solidCells",solid);
        env.addProperty("light",mc.world.getLightLevel(center)); env.addProperty("raining",mc.world.isRaining());
        env.addProperty("hostiles",Math.min(20,mc.world.getEntitiesByClass(HostileEntity.class,mc.player.getBoundingBox().expand(8),e->e.isAlive()).size()));
        JsonObject ways=new JsonObject();
        for (String d:new String[]{"forward","back","left","right"}) ways.addProperty(d,safeStep(mc,step(mc,d)));
        env.add("safeSteps",ways);
        JsonObject materials=new JsonObject();
        for (int i=0;i<9;i++) {
            var stack=mc.player.getInventory().getStack(i);
            String id=Registries.ITEM.getId(stack.getItem()).toString();
            if (Blueprints.MATERIALS.contains(id)) materials.addProperty(id, (materials.has(id)?materials.get(id).getAsInt():0)+stack.getCount());
        }
        env.add("hotbarMaterials",materials);
        return env;
    }
}
