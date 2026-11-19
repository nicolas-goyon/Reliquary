-- A user signs in only with an OAuth provider. See docs/decisions/0054-sign-in-with-oauth-providers-only.md.
-- The accounts have no password, the instance sends no email, and the provider checks the email address.
drop table users.account_tokens;

alter table users.accounts
    drop column password_hash,
    drop column email_checked_at;
