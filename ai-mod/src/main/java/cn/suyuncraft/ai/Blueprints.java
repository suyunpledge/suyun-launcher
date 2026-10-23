package cn.suyuncraft.ai;

import java.util.*;

/** Pure geometry: local x=left, y=up, z=forward; never model-supplied coordinates. */
public final class Blueprints {
    public static final Set<String> NAMES = Set.of("floor3", "wall3", "pillar3", "shelter3");
    public static final Set<String> MATERIALS = Set.of("minecraft:cobblestone", "minecraft:stone", "minecraft:dirt",
        "minecraft:oak_planks", "minecraft:glass", "minecraft:bricks", "minecraft:sandstone");
    public record Cell(int x, int y, int z) {
        public Cell rotate(int quarterTurns) {
            return switch (Math.floorMod(quarterTurns, 4)) {
                case 1 -> new Cell(-z, y, x); case 2 -> new Cell(-x, y, -z);
                case 3 -> new Cell(z, y, -x); default -> this;
            };
        }
    }
    public static List<Cell> cells(String name) {
        if (!NAMES.contains(name)) throw new IllegalArgumentException("未知蓝图");
        var cells = new LinkedHashSet<Cell>();
        if (name.equals("floor3") || name.equals("shelter3"))
            for (int z=1; z<=3; z++) for (int x=-1; x<=1; x++) cells.add(new Cell(x,0,z));
        if (name.equals("wall3"))
            for (int y=0; y<2; y++) for (int x=-1; x<=1; x++) cells.add(new Cell(x,y,2));
        if (name.equals("pillar3")) for (int y=0; y<3; y++) cells.add(new Cell(0,y,2));
        if (name.equals("shelter3")) {
            for (int y=1; y<3; y++) for (int z=1; z<=3; z++) for (int x=-1; x<=1; x++)
                if ((x != 0 || z == 1 || z == 3) && !(x==0 && z==1)) cells.add(new Cell(x,y,z));
            for (int z=1; z<=3; z++) for (int x=-1; x<=1; x++) cells.add(new Cell(x,3,z));
        }
        return List.copyOf(cells);
    }
}
