package cn.suyuncraft.ai.mixin;

import cn.suyuncraft.ai.AiClient;
import net.minecraft.client.network.ClientPlayNetworkHandler;
import net.minecraft.network.packet.s2c.play.BlockUpdateS2CPacket;
import net.minecraft.network.packet.s2c.play.ChunkDeltaUpdateS2CPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPlayNetworkHandler.class)
public class WorldUpdateMixin {
    @Inject(method="onBlockUpdate",at=@At("TAIL"))
    private void suyuncraft$block(BlockUpdateS2CPacket packet,CallbackInfo ci) {
        AiClient.serverBlockUpdate(packet.getPos(),packet.getState());
    }
    @Inject(method="onChunkDeltaUpdate",at=@At("TAIL"))
    private void suyuncraft$delta(ChunkDeltaUpdateS2CPacket packet,CallbackInfo ci) {
        packet.visitUpdates(AiClient::serverBlockUpdate);
    }
}
