# LupiraContactApi

Contacts, address books, and kinship for the Lupira platform. Extracted from LupiraCalApi; the
CardDAV protocol surface lives in the LupiraDavApi gateway, which consumes this service's
LAN-only [`/dav-backend` seam](docs/dav-backend-contract.md).

- **REST at root** (`https://contact-api.lupira.com`) — contacts CRUD + query, contact-to-contact
  relations with inferred kinship, contact groups (personal + organization), address books with
  multi-owner grants, `/me` + `/me/bootstrap`.
- **MCP at `/mcp`** (LAN/WireGuard-only) — agent tools: `query_contacts`, `create_contact`,
  `relate_contacts`, `unrelate_contacts`, `list_contact_relations`, `list_address_books`,
  `create_address_book`, `bootstrap_me`, `grant_addressbook_owner`, `revoke_addressbook_owner`.
- **`/internal/contacts/resolve`** (LAN-only, service-authed) — existence + display-name lookup for
  sibling services (cal-api's `IContactResolver`).
- **`/dav-backend`** (LAN-only, DAV-gateway-authed) — collections/resources/changes for CardDAV.

## Architecture

.NET 10 minimal APIs. Marten 9.x event store + documents on Postgres, schema `contact`:
event-sourced `Contact`/`ContactGroup` (inline snapshots), plain `AddressBook`/`AddressBookOwner`/
`Principal` docs. Hand-rolled vCard 3.0 serializer (`RELATED` round-trip incl. `X-LUPIRA-LABEL`).
Auth: Authentik OIDC (aud `lupira-contact`); `X-Dev-User` header in Development.

## Develop

```bash
dotnet test LupiraContactApi.slnx                       # unit (fast, no I/O)
dotnet test tests/LupiraContactApi.IntegrationTests     # WebApplicationFactory + Testcontainers PG
```

Schema apply (deploy step, not on boot): `dotnet LupiraContactApi.dll --apply-schema`.

OpenAPI at `/openapi/v1.json`, interactive docs at `/scalar/v1`. Deployment config:
DevOps repo `APIs/lupira-contact-api/` (authoritative); [`deploy/`](deploy/) is a genericized mirror.
