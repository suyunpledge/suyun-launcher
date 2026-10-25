package cn.suyuncraft.ai;

public final class CommandTests {
    private static int checks;
    private static void check(boolean ok,String name) {if(!ok)throw new AssertionError(name);checks++;}
    public static void main(String[] args) {
        check(Commands.allowed("time set day"),"time set day allowed");
        check(Commands.allowed("weather clear"),"weather clear allowed");
        check(Commands.allowed("gamemode creative"),"gamemode creative allowed");
        check(Commands.allowed("difficulty hard"),"difficulty hard allowed");
        check(Commands.allowed("tp 100 64 -200"),"tp with coordinates allowed");
        check(Commands.allowed("effect give @s speed 30 1"),"effect with selector and args allowed");
        check(Commands.allowed("  Time set night  "),"whitespace trimmed and roots case-insensitive");
        // Whitelist: chat-ish, destructive or unknown roots never pass even with the right shape.
        check(!Commands.allowed("tell @a hello"),"chat root rejected");
        check(!Commands.allowed("stop"),"server control root rejected");
        check(!Commands.allowed("kill @e"),"kill root rejected");
        check(!Commands.allowed("op PlayerName"),"op root rejected");
        // Shape: leading slash, injection characters, overlong bodies and arg floods are rejected.
        check(!Commands.allowed("/time set day"),"leading slash rejected");
        check(!Commands.allowed("time set day;kill"),"semicolon rejected");
        check(!Commands.allowed("time set day&&weather rain"),"shell operators rejected");
        check(!Commands.allowed("time " + "a".repeat(120)),"overlong body rejected");
        check(!Commands.allowed("time " + String.join(" ", "1 2 3 4 5 6 7 8 9")),"too many arguments rejected");
        check(!Commands.allowed("gamemode créative"),"non-ascii rejected");
        check(!Commands.allowed(""),"empty rejected");
        check(!Commands.allowed(null),"null rejected");
        System.out.println("Command checks passed: "+checks);
    }
}
