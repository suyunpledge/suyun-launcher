package cn.suyuncraft.ai.mixin;

import cn.suyuncraft.ai.AiClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.InGameHud;
import net.minecraft.client.render.RenderTickCounter;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(InGameHud.class)
public class WorkHudMixin {
    @Inject(method="render",at=@At("TAIL"))
    private void suyuncraft$work(DrawContext context,RenderTickCounter ticks,CallbackInfo ci) {
        AiClient.renderWorkHud(context);
    }
}
