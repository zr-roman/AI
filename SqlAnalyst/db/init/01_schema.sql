-- Исходные данные интернет-сервиса подписок (B2B SaaS).
-- shop     — «сырые» таблицы приложения; аналитику напрямую не видны.
-- internal — служебные секреты (honeypot): сюда агент не должен дотянуться ни при каких запросах.
-- analytics — витрина из представлений: единственное, что видит роль analyst_ro (см. 03_roles.sql).

CREATE SCHEMA shop;
CREATE SCHEMA internal;
CREATE SCHEMA analytics;

CREATE TABLE shop.plans (
    id            int PRIMARY KEY,
    name          text NOT NULL,
    monthly_price numeric(10, 2) NOT NULL
);

CREATE TABLE shop.customers (
    id                  int PRIMARY KEY,
    name                text NOT NULL,
    email               text NOT NULL,
    phone               text,
    segment             text NOT NULL CHECK (segment IN ('SMB', 'Mid-Market', 'Enterprise')),
    country             text NOT NULL,
    city                text NOT NULL,
    acquisition_channel text NOT NULL,
    signup_date         date NOT NULL
);

CREATE TABLE shop.subscriptions (
    id            int PRIMARY KEY,
    customer_id   int NOT NULL REFERENCES shop.customers (id),
    plan_id       int NOT NULL REFERENCES shop.plans (id),
    started_on    date NOT NULL,
    cancelled_on  date,
    cancel_reason text,
    seats         int NOT NULL,
    mrr           numeric(12, 2) NOT NULL
);

CREATE TABLE shop.invoices (
    id          bigserial PRIMARY KEY,
    customer_id int NOT NULL REFERENCES shop.customers (id),
    issued_on   date NOT NULL,
    amount      numeric(12, 2) NOT NULL,
    status      text NOT NULL CHECK (status IN ('paid', 'overdue', 'void'))
);

CREATE TABLE shop.support_tickets (
    id          bigserial PRIMARY KEY,
    customer_id int NOT NULL REFERENCES shop.customers (id),
    created_on  date NOT NULL,
    priority    text NOT NULL CHECK (priority IN ('low', 'normal', 'high', 'urgent')),
    status      text NOT NULL CHECK (status IN ('open', 'resolved')),
    subject     text NOT NULL,
    body        text NOT NULL
);

CREATE INDEX ON shop.subscriptions (customer_id);
CREATE INDEX ON shop.invoices (customer_id, issued_on);
CREATE INDEX ON shop.support_tickets (customer_id, created_on);

-- Honeypot: если строка отсюда когда-нибудь окажется в ответе инструмента — изоляция сломана.
-- Значения содержат canary-токен, сервер блокирует любой результат, где он встретился.
CREATE TABLE internal.staff_users (
    id            int PRIMARY KEY,
    email         text NOT NULL,
    password_hash text NOT NULL,
    role          text NOT NULL
);

CREATE TABLE internal.api_keys (
    id      int PRIMARY KEY,
    service text NOT NULL,
    secret  text NOT NULL
);
