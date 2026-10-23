package cn.suyuncraft.ai;

public final class DialogueTests {
    private static int checks;
    private static void check(boolean ok,String name) {if(!ok)throw new AssertionError(name);checks++;}
    public static void main(String[] args) {
        var log=new ConversationLog();
        log.add("user","帮我规划墙"); log.add("status","正在规划"); log.add("plan","检查材料"); log.add("assistant","可以用 wall3");
        check(log.lines().size()==4,"all public events visible locally");
        check(log.context().size()==2,"status and plan do not inflate remote history");
        check(log.context().get(1).role().equals("assistant"),"preserve dialogue roles");
        for(int i=0;i<10;i++)log.add(i%2==0?"user":"assistant","turn"+i);
        check(log.context().size()==4 && log.context().get(0).text().equals("turn6"),"only last four conversational messages sent");
        log.add("assistant","长".repeat(1000));
        check(log.lines().get(log.lines().size()-1).text().length()==600,"bounded local message length");
        check(log.context().get(3).text().length()==160,"bounded API history message length");
        check(log.context().stream().mapToInt(e->e.text().length()).sum()<=640,"total history character budget");
        log.clear(); check(log.lines().isEmpty() && log.context().isEmpty(),"clear removes local and remote history");
        for(int i=0;i<100;i++)log.add("status","s"+i);
        check(log.lines().size()==60 && log.lines().get(0).text().equals("s40"),"UI ring buffer evicts old events");
        check(log.context().isEmpty(),"long execution log sends no chat history");
        log.add("user",""); log.add("assistant",null);
        check(log.lines().size()==60,"ignore empty events");
        System.out.println("Dialogue checks passed: "+checks);
    }
}
