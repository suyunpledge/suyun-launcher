package cn.suyuncraft.ai;

public final class MotionTests {
    private static int checks;
    private static void check(boolean ok,String name) {if(!ok)throw new AssertionError(name);checks++;}
    private static void close(float a,float b,String name){check(Math.abs(a-b)<0.001f,name+" got "+a);}
    public static void main(String[] args) {
        check(Motion.wrapDegrees(190f)==-170f,"wrap 190 -> -170");
        check(Motion.wrapDegrees(-190f)==170f,"wrap -190 -> 170");
        close(Motion.wrapDegrees(540f),-180f,"wrap 540 -> -180");
        close(Motion.wrapDegrees(0f),0f,"wrap zero unchanged");
        // Bounded steps never snap: a large turn advances by the per-tick budget only.
        close(Motion.turnTowards(0f,90f,Motion.YAW_SPEED),Motion.YAW_SPEED,"first tick of a 90 degree turn");
        close(Motion.turnTowards(0f,10f,Motion.YAW_SPEED),10f,"small turn lands exactly on target");
        close(Motion.turnTowards(50f,20f,Motion.YAW_SPEED),32f,"negative direction steps back");
        check(Motion.WALK_YAW_SPEED<Motion.YAW_SPEED,"walking turns slower than free looking");
        // Shortest path crosses the +/-180 seam instead of going the long way round.
        float yaw=170f;
        yaw=Motion.turnTowards(yaw,-170f,Motion.YAW_SPEED); close(yaw,188f,"seam step one");
        yaw=Motion.turnTowards(yaw,-170f,Motion.YAW_SPEED); close(yaw,190f,"seam step two completes the shortest path");
        close(Motion.wrapDegrees(yaw),-170f,"wrapped presentation of past-180 yaw");
        // Vanilla yaw convention: 0 = +Z, 90 = -X; pitch negative looks up.
        close(Motion.aimYaw(0,1),0f,"yaw facing +Z");
        close(Motion.aimYaw(-1,0),90f,"yaw facing -X");
        close(Motion.aimYaw(1,-1),-135f,"yaw facing +X-Z");
        close(Motion.aimPitch(1,1,1),(float)-Math.toDegrees(Math.atan2(1,Math.sqrt(2))),"pitch up is negative");
        close(Motion.aimPitch(1,-1,1),(float)Math.toDegrees(Math.atan2(1,Math.sqrt(2))),"pitch down is positive");
        // Arrival tolerances gate the end of an aim, not exact equality.
        check(Motion.arrived(10f,0f,10.5f,0.5f),"within arrival tolerance");
        check(!Motion.arrived(10f,0f,30f,0f),"far from arrival");
        check(!Motion.arrived(0f,0f,0f,5f),"pitch still off");
        System.out.println("Motion checks passed: "+checks);
    }
}
