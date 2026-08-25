# ile-3 — Functions without domain

**Depends on:** ile-2  

A language VM needs callable functions that are not `DomainEntityInstance`.

- Lambdas + `Invoke(Lambda)` already exist — pin with VM tests (captures, return, arity mismatch fail-closed).  
- `TypeDefinitionNode` instance/static methods: invoke via resolved member / name+arity on a heap instance that is **not** a domain bag unless the test supplies one as a CLR object.  
- No `DomainResult` in Interpretation. Failure is throw or a user-level object the program created.
