# Local setup (for collaborators)

Follow these steps after cloning / pulling the repo.

## Prerequisites
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- **SQL Server LocalDB** (Windows) — installed with Visual Studio, or the standalone
  "SQL Server Express LocalDB" package. (On macOS/Linux, use any SQL Server instance and
  change the connection string accordingly.)

## Steps
1. **Copy the config template** to the real (git-ignored) dev config file:

   ```bash
   copy appsettings.Development.example.json appsettings.Development.json
   ```
   > `appsettings.Development.json` holds local secrets and is **git-ignored on purpose**,
   > so it is NOT in the repo. That's why you must create it yourself.

2. *(Optional)* To test **checkout**, put your own **Stripe TEST keys** in the `Stripe`
   section of `appsettings.Development.json` (`SecretKey`, `PublishableKey`, and a
   `WebhookSecret`). To test **email/OTP**, fill in the `Email` section. The app runs fine
   without these — checkout/email are just disabled until configured.

3. **Run:**
   ```bash
   dotnet run
   ```
   The database is **created and seeded automatically** on first run (migrations apply at
   startup). No manual DB setup needed.

4. Open **http://localhost:5156**

## Seeded admin login
- Email: `admin@worldlinkmaster.com`
- Password: `Admin@12345`

## Seeded promo codes (try at checkout)
`WELCOME15` (15%), `SUMMER20` (20%), `FLASH30` (30%, first 100 orders).
