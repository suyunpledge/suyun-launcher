package cn.suyuncraft.ai;

import java.util.LinkedHashSet;
import java.util.function.Consumer;

/** Release only controls the AI touched; idle ticks must not change other input. */
final class InputLease<T> {
    private final LinkedHashSet<T> held = new LinkedHashSet<>();
    void hold(T key, Consumer<T> press) { held.add(key); press.accept(key); }
    void release(Consumer<T> release) {
        for (T key : held) release.accept(key);
        held.clear();
    }
}
