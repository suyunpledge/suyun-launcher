package cn.suyuncraft.ai;

import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * Bounded chat-command gate: a fixed root whitelist plus a safe charset, shared by
 * response validation and execution. Only slash-free command bodies pass; the mod
 * forwards them via the network handler, never the chat stream.
 */
public final class Commands {
    public static final Set<String> ROOTS = Set.of("time", "weather", "gamemode", "difficulty", "tp", "effect");
    private static final Pattern SHAPE = Pattern.compile("^[A-Za-z0-9_\\-:.@ ]{1,100}$");

    private Commands() { }

    public static boolean allowed(String command) {
        if (command == null) return false;
        String trimmed = command.trim();
        if (trimmed.isEmpty() || trimmed.startsWith("/") || trimmed.length() > 100 || !SHAPE.matcher(trimmed).matches())
            return false;
        String[] parts = trimmed.split("\\s+");
        return parts.length <= 8 && ROOTS.contains(parts[0].toLowerCase(Locale.ROOT));
    }
}
