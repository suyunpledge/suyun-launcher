package cn.suyuncraft.ai;
import java.util.*;

/** Runs without Minecraft or paid APIs; executed as part of Gradle check. */
public final class BlueprintTests {
    static int checks;
    static void check(boolean ok,String name) { if(!ok) throw new AssertionError(name); checks++; }
    public static void main(String[] args) {
        Map<String,Integer> sizes=Map.of("floor3",9,"wall3",6,"pillar3",3,"shelter3",32);
        for(String name:Blueprints.NAMES) {
            var cells=Blueprints.cells(name);
            check(cells.size()==sizes.get(name),name+" material budget");
            check(new HashSet<>(cells).size()==cells.size(),name+" no duplicate targets");
            check(cells.stream().allMatch(c->c.z()>=1 && Math.abs(c.x())<=1 && c.z()<=3 && c.y()>=0 && c.y()<=3),name+" bounded and in front of player");
            for(int r=0;r<4;r++) {
                int rotation=r;
                check(cells.stream().map(c->c.rotate(rotation)).distinct().count()==cells.size(),name+" rotation cardinality");
                check(cells.stream().allMatch(c->c.rotate(rotation).rotate(-rotation).equals(c)),name+" rotation roundtrip");
            }
            // Every cell can be reached by growth from a ground-supported cell; no floating roof cells.
            var connected=new HashSet<Blueprints.Cell>();
            cells.stream().filter(c->c.y()==0).forEach(connected::add);
            boolean changed=true;
            while(changed) {
                changed=false;
                for(var c:cells) if(!connected.contains(c) && connected.stream().anyMatch(p->Math.abs(p.x()-c.x())+Math.abs(p.y()-c.y())+Math.abs(p.z()-c.z())==1)) {connected.add(c);changed=true;}
            }
            check(connected.size()==cells.size(),name+" supported growth topology");
        }
        var shelter=Blueprints.cells("shelter3");
        check(!shelter.contains(new Blueprints.Cell(0,1,1)) && !shelter.contains(new Blueprints.Cell(0,2,1)),"two-block doorway remains open");
        check(!shelter.contains(new Blueprints.Cell(0,1,2)) && !shelter.contains(new Blueprints.Cell(0,2,2)),"interior is not filled");
        boolean rejected=false; try{Blueprints.cells("castle999");}catch(IllegalArgumentException e){rejected=true;}
        check(rejected,"unsupported arbitrary blueprint rejected");
        var receipt=new PlacementReceipt(); receipt.reset();
        for(int i=0;i<60;i++) check(receipt.tick()==PlacementReceipt.Result.WAITING,"prediction alone does not acknowledge placement");
        check(receipt.tick()==PlacementReceipt.Result.TIMEOUT,"missing server packet times out");
        receipt.resume(); check(receipt.tick()==PlacementReceipt.Result.WAITING,"resume only waits without resending");
        receipt.observe(true); check(receipt.tick()==PlacementReceipt.Result.CONFIRMED,"server confirms expected block");
        receipt.reset(); check(receipt.tick()==PlacementReceipt.Result.WAITING,"next placement does not reuse previous ack");
        receipt.observe(false); check(receipt.tick()==PlacementReceipt.Result.REJECTED,"server correction is rejection");
        System.out.println("Blueprint/receipt checks passed: "+checks);
    }
}
