package cn.suyuncraft.ai;

import java.util.*;

/** Bounded, session-only UI transcript. Status events never become model history. */
final class ConversationLog {
    record Entry(String role,String text) { }
    private final ArrayDeque<Entry> entries=new ArrayDeque<>();
    void add(String role,String text) {
        if(text==null || text.isBlank()) return;
        entries.addLast(new Entry(role,text.substring(0,Math.min(600,text.length()))));
        while(entries.size()>60) entries.removeFirst();
    }
    List<Entry> lines() { return List.copyOf(entries); }
    List<Entry> context() {
        var messages=entries.stream().filter(e->e.role().equals("user") || e.role().equals("assistant")).toList();
        var result=new ArrayList<Entry>();
        for(int i=Math.max(0,messages.size()-4);i<messages.size();i++) {
            var e=messages.get(i); result.add(new Entry(e.role(),e.text().substring(0,Math.min(160,e.text().length()))));
        }
        return List.copyOf(result);
    }
    void clear() { entries.clear(); }
}
