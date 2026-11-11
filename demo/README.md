# Poly Demo

The demo is a generated HTTP host, not a hand-written console.

```bash
scripts/live-demo.sh
```

That compiles `docs/probes/fleet-eval/09-transport/warehouse.poly` to a SQLite Minimal API, then walks three calls: create a warehouse, read it back, and register a truck.

To leave the host running:

```bash
scripts/serve-poly.sh docs/probes/fleet-eval/09-transport/warehouse.poly
```

Pass another `.poly` to serve that domain instead. `demo.http` in the output directory has sample requests. Sample bodies can violate constraints; use values the domain accepts.

`demo/live/domain.poly` is the pairing document (apply it in a session, then serve that file). `demo/Poly.RestApi` is the library host generated from `demo/Poly.RestApi/library.poly`; `EmitGoldenTests` asserts it equals fresh compiler output. Regenerate with `polyc build demo/Poly.RestApi/library.poly --out demo`.
