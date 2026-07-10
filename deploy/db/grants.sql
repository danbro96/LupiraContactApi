-- lupira-contact-api: provision the `lupira_contact` database on the shared medelynas-db.
-- One role, one logical database. Marten owns the `contact` schema, tables, and indexes — all created by
-- `--apply-schema` (one-shot deploy step), not here.
--
-- Apply (TrueNAS Shell), substituting a freshly generated password:
--   LUPIRA_CONTACT_DB_PW="$(openssl rand -hex 32)"; echo "$LUPIRA_CONTACT_DB_PW"   # save to your password manager
--   docker exec -i medelynas-db psql -U medelynas_admin -v app_password="'$LUPIRA_CONTACT_DB_PW'" postgres < grants.sql

CREATE ROLE lupira_contact_user WITH LOGIN PASSWORD :'app_password';
CREATE DATABASE lupira_contact OWNER lupira_contact_user;
REVOKE ALL ON DATABASE lupira_contact FROM PUBLIC;
GRANT CONNECT ON DATABASE lupira_contact TO lupira_contact_user;
