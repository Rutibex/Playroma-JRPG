# Raw-Backed Typed MV Views

Typed views are navigation and mutation adapters over a
`MutableJsonProjectDocument`. They do not own a separately deserialized copy.

```csharp
MvActorView actor = actors.Root.GetById(1)!;
actor.Name = "Theron";
```

The assignment records a replacement at `/1/name`. Plugin-owned fields beside
that property remain in their original source spans.

A missing property may be exposed through a safe default for reading, but a
read never inserts the default into the file. Only an explicit setter or
structural operation changes the document.

`MvIdArray<TView>` preserves physical array slots and distinguishes an absent
slot from a JSON null slot. It never automatically fixes a record whose `id`
differs from its array index.
