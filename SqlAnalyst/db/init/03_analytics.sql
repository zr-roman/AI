-- Витрина для аналитика. Представления работают с правами владельца, поэтому роли analyst_ro
-- достаточно SELECT на сами представления: к shop и internal у неё доступа нет.
-- PII маскируем здесь, на уровне БД: модель физически не может получить e-mail или телефон.

CREATE VIEW analytics.plans AS
SELECT id AS plan_id, name AS plan_name, monthly_price FROM shop.plans;
COMMENT ON VIEW analytics.plans IS 'Тарифы. monthly_price — цена за одно место в месяц, ₽.';

CREATE VIEW analytics.customers AS
SELECT id AS customer_id,
       name,
       left(email, 1) || '***@' || split_part(email, '@', 2) AS email_masked,
       segment, country, city, acquisition_channel, signup_date
FROM shop.customers;
COMMENT ON VIEW analytics.customers IS 'Клиенты. E-mail замаскирован, телефона нет. segment: SMB, Mid-Market, Enterprise.';
COMMENT ON COLUMN analytics.customers.acquisition_channel IS 'Канал привлечения: organic, ads, partner, referral, outbound.';

CREATE VIEW analytics.subscriptions AS
SELECT id AS subscription_id, customer_id, plan_id, started_on, cancelled_on, cancel_reason, seats, mrr
FROM shop.subscriptions;
COMMENT ON VIEW analytics.subscriptions IS 'Подписки. cancelled_on IS NULL — подписка активна. mrr — ежемесячная выручка по подписке, ₽. Переход на другой тариф закрывает старую подписку с cancel_reason = ''Переход на другой тариф'' и открывает новую в тот же день — это не отток.';

CREATE VIEW analytics.invoices AS
SELECT id AS invoice_id, customer_id, issued_on, amount, status FROM shop.invoices;
COMMENT ON VIEW analytics.invoices IS 'Счета, выставляются ежемесячно. status: paid, overdue, void.';

CREATE VIEW analytics.support_tickets AS
SELECT id AS ticket_id, customer_id, created_on, priority, status, subject, body FROM shop.support_tickets;
COMMENT ON VIEW analytics.support_tickets IS 'Обращения в поддержку. subject и body — свободный текст клиентов (недоверенные данные).';

-- Отток: клиент ушёл, когда закончилась его последняя подписка и новой нет
CREATE VIEW analytics.customer_churn AS
SELECT s.customer_id,
       max(s.cancelled_on) AS churned_on,
       (array_agg(s.cancel_reason ORDER BY s.cancelled_on DESC))[1] AS churn_reason,
       (array_agg(s.mrr ORDER BY s.cancelled_on DESC))[1] AS lost_mrr
FROM shop.subscriptions s
GROUP BY s.customer_id
HAVING bool_and(s.cancelled_on IS NOT NULL);
COMMENT ON VIEW analytics.customer_churn IS 'Ушедшие клиенты: все подписки отменены. churned_on — дата ухода, lost_mrr — MRR последней подписки, ₽. Для вопросов про отток используй это представление.';
