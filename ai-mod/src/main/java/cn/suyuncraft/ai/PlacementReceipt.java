package cn.suyuncraft.ai;

/** Only server packet callbacks may observe a result; rendered prediction is not an acknowledgment. */
final class PlacementReceipt {
    enum Result { WAITING, CONFIRMED, REJECTED, TIMEOUT }
    private Boolean accepted;
    private int age;
    void reset() { accepted=null; age=0; }
    void resume() { age=0; }
    void observe(boolean matches) { accepted=matches; }
    Result tick() {
        if (accepted!=null) return accepted?Result.CONFIRMED:Result.REJECTED;
        return ++age>60?Result.TIMEOUT:Result.WAITING;
    }
}
