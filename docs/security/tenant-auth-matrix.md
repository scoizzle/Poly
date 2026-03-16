# Tenant and Authorization Scope Matrix

This matrix defines role permissions for v1 operations.

## Roles
- `tenant_reader`
- `tenant_editor`
- `tenant_publisher`
- `tenant_owner`
- `platform_admin`

## Scope Rules
- Tenant-private by default.
- Cross-tenant read/evaluate requires an explicit active grant.
- Roles do not override tenant boundary checks.
- `platform_admin` is reserved for platform-level management APIs.

## Operation Matrix

| Operation | tenant_reader | tenant_editor | tenant_publisher | tenant_owner | platform_admin |
|---|---:|---:|---:|---:|---:|
| Read model metadata (own tenant) | Y | Y | Y | Y | Y |
| Evaluate model (own tenant) | Y | Y | Y | Y | Y |
| Read model metadata (cross-tenant with grant) | Y | Y | Y | Y | Y |
| Evaluate model (cross-tenant with grant) | Y | Y | Y | Y | Y |
| Create/revoke model grants | N | N | N | Y | Y |
| Stage model | N | Y | Y | Y | Y |
| Publish/deprecate/sunset model | N | N | Y | Y | Y |
| Import extension contracts | N | Y | Y | Y | Y |
| Manage API keys (create/revoke/list) | N | N | N | Y | Y |
| Manage tenant approval policy | N | N | N | Y | Y |
| Provision/deactivate tenants | N | N | N | N | Y |

## Notes
- All operations require successful tenant resolution first.
- Authorization failure must return `AUTH.INSUFFICIENT_SCOPE`.
- Tenant boundary failure must return `AUTH.TENANT_MISMATCH` or `AUTH.GRANT_REQUIRED`.
