-- Роль, от имени которой работает MCP-сервер. Это главный рубеж защиты:
-- даже если валидатор SQL пропустит что-то лишнее, БД сама не даст писать и читать чужие схемы.
CREATE ROLE analyst_ro LOGIN PASSWORD 'analyst_ro'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS
    CONNECTION LIMIT 10;

ALTER ROLE analyst_ro SET default_transaction_read_only = on;
ALTER ROLE analyst_ro SET statement_timeout = '5s';
ALTER ROLE analyst_ro SET lock_timeout = '1s';
ALTER ROLE analyst_ro SET idle_in_transaction_session_timeout = '10s';
ALTER ROLE analyst_ro SET work_mem = '16MB';
ALTER ROLE analyst_ro SET temp_file_limit = '64MB';
ALTER ROLE analyst_ro SET search_path = analytics;

DO $$
BEGIN
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO analyst_ro', current_database());
END $$;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON SCHEMA shop, internal FROM PUBLIC;
GRANT USAGE ON SCHEMA analytics TO analyst_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA analytics TO analyst_ro;

-- Функции, которыми read-only запрос всё равно может навредить: подвесить соединение,
-- поменять настройки сессии, взять advisory-блокировку, слать уведомления, выполнить SQL из строки.
DO $$
DECLARE
    f regprocedure;
BEGIN
    FOR f IN
        SELECT p.oid::regprocedure
        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'pg_catalog'
          AND (p.proname IN ('pg_sleep', 'pg_sleep_for', 'pg_sleep_until', 'set_config', 'pg_notify',
                             'query_to_xml', 'query_to_xml_and_xmlschema', 'query_to_xmlschema',
                             'cursor_to_xml', 'cursor_to_xmlschema', 'pg_stat_get_activity')
               OR p.proname LIKE 'pg\_advisory%' OR p.proname LIKE 'pg\_try\_advisory%'
               OR p.proname LIKE 'lo\_%' OR p.proname LIKE 'dblink%')
    LOOP
        EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', f);
    END LOOP;
END $$;
