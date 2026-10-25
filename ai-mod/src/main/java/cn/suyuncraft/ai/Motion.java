package cn.suyuncraft.ai;

/** Pure motion math for smooth turning; no Minecraft classes so verification tests can run offline. */
public final class Motion {
    /** Per-tick angular speeds in degrees: a full 360 turn takes about one second. */
    public static final float YAW_SPEED = 18f, PITCH_SPEED = 12f;
    /** Rotation is considered finished within these residuals. */
    public static final float YAW_ARRIVAL = 1.2f, PITCH_ARRIVAL = 0.8f;
    /** Walking turns slower than free looking so the view stays stable while moving. */
    public static final float WALK_YAW_SPEED = 10f;

    private Motion() { }

    public static float wrapDegrees(float angle) {
        angle %= 360f;
        if (angle >= 180f) angle -= 360f;
        else if (angle < -180f) angle += 360f;
        return angle;
    }

    /** Shortest-path bounded step from current toward target, never snapping. */
    public static float turnTowards(float current, float target, float maxStep) {
        float delta = wrapDegrees(target - current);
        if (delta > maxStep) delta = maxStep;
        else if (delta < -maxStep) delta = -maxStep;
        return current + delta;
    }

    /** Yaw for a horizontal offset: 0 = +Z, 90 = -X, matching the vanilla convention. */
    public static float aimYaw(double dx, double dz) {
        return (float) Math.toDegrees(Math.atan2(-dx, dz));
    }

    /** Pitch for an offset; negative looks up, matching the vanilla convention. */
    public static float aimPitch(double dx, double dy, double dz) {
        return (float) -Math.toDegrees(Math.atan2(dy, Math.sqrt(dx * dx + dz * dz)));
    }

    public static boolean arrived(float yaw, float pitch, float targetYaw, float targetPitch) {
        return Math.abs(wrapDegrees(targetYaw - yaw)) <= YAW_ARRIVAL && Math.abs(targetPitch - pitch) <= PITCH_ARRIVAL;
    }
}
