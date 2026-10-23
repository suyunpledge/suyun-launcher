package cn.suyuncraft.ai;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.gui.widget.TextFieldWidget;
import net.minecraft.text.Text;
import net.minecraft.text.OrderedText;
import org.lwjgl.glfw.GLFW;
import java.util.*;

final class AiChatScreen extends Screen {
    private final AiClient ai;
    private TextFieldWidget input;
    private int scroll;
    private List<ConversationLog.Entry> rendered=List.of();
    private final List<OrderedText> cachedLines=new ArrayList<>();
    private int renderedWidth;
    AiChatScreen(AiClient ai) { super(Text.literal("Suyun AI 对话与工作")); this.ai=ai; }
    protected void init() {
        String draft=input==null?"":input.getText();
        input=addDrawableChild(new TextFieldWidget(textRenderer,16,height-80,Math.max(40,width-32),20,Text.literal("输入问题或任务")));
        input.setMaxLength(300); input.setText(draft); setInitialFocus(input);
        int w=Math.max(40,(width-38)/4);
        addDrawableChild(ButtonWidget.builder(Text.literal("对话"),b->send(false)).dimensions(16,height-56,w,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("交给AI执行"),b->send(true)).dimensions(18+w,height-56,w,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("确认/继续"),b->{client.setScreen(null); ai.confirmDialog();}).dimensions(20+2*w,height-56,w,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("停止"),b->ai.stopDialog()).dimensions(22+3*w,height-56,w,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("返回游戏"),b->close()).dimensions(16,height-30,w*2,20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("清空对话"),b->{ai.clearDialog();scroll=0;}).dimensions(22+2*w,height-30,w*2,20).build());
    }
    private void send(boolean work) {
        String text=input.getText().trim(); if(text.isEmpty()) return;
        ai.sendDialog(text,work); input.setText(""); scroll=0;
        if(work) client.setScreen(null); // Return control before the first action tick.
    }
    public boolean shouldPause() { return false; }
    public boolean keyPressed(int key,int scan,int modifiers) {
        if(key==GLFW.GLFW_KEY_ENTER || key==GLFW.GLFW_KEY_KP_ENTER) {send(Screen.hasControlDown());return true;}
        return super.keyPressed(key,scan,modifiers);
    }
    public boolean mouseScrolled(double x,double y,double horizontal,double vertical) {
        scroll=Math.max(0,scroll+(int)Math.signum(vertical)*3); return true;
    }
    public void render(DrawContext ctx,int mouseX,int mouseY,float delta) {
        ctx.fill(0,0,width,height,0xE6141C29);
        ctx.drawTextWithShadow(textRenderer,title,16,10,0xFFFFFF);
        ctx.drawTextWithShadow(textRenderer,textRenderer.trimToWidth(ai.dialogStatus(),Math.max(20,width-32)),16,26,0x98D6A5);
        var snapshot=ai.dialogLines();
        if(renderedWidth!=width || !snapshot.equals(rendered)) {
            cachedLines.clear(); rendered=snapshot; renderedWidth=width;
            for(var e:snapshot) {
                String label=switch(e.role()){case "user"->"你";case "assistant"->"AI";case "plan"->"计划";default->"状态";};
                cachedLines.addAll(textRenderer.wrapLines(Text.literal(label+"："+e.text()),Math.max(20,width-40)));
            }
        }
        var lines=cachedLines;
        int rows=Math.max(1,(height-134)/11);
        scroll=Math.min(scroll,Math.max(0,lines.size()-rows));
        int end=Math.max(0,lines.size()-scroll), start=Math.max(0,end-rows);
        ctx.enableScissor(16,43,width-16,Math.max(44,height-88));
        for(int i=start;i<end;i++)ctx.drawTextWithShadow(textRenderer,lines.get(i),20,45+(i-start)*11,0xDCE4EE);
        ctx.disableScissor();
        super.render(ctx,mouseX,mouseY,delta);
    }
}
