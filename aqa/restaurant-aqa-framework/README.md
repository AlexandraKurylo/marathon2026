# Restaurant AQA framework

## Рішення ДЗ: напрям 1 «Груповий pre-order»

**Обраний напрям:** Напрям 1. Бекенд-ДЗ (рівні 1–3) реалізоване й розгорнуте разом з фронтендом. У фронтенді немає екрана групового pre-order, тому всі сценарії рівнів перевіряються через API, а мінімальний UI-тест — наявні `@ui`-сценарії входу й меню (`Auth.feature`, `MenuCatalog.feature`, `Registration.feature`).

**Підхід:** BDD (Reqnroll) + Screenplay, як у наявному фреймворку. Сценарії лежать у `Features/GroupPreOrder.feature`, кроки в `StepDefinitions/GroupPreOrderStepDefinitions.cs`, дії актора в `Screenplay/Tasks/` (`BookATableForAGroup`, `JoinTheGroup`, `PutADishInTheCart`, `ConfirmTheCart`, `LookAtTheParticipants`, `ConfirmTheGroupPreOrder`, `RemoveTheParticipant`, `AskForTheOwnersPlace`, `HireAWaiter`), API-клієнти в `Api/GroupPreOrderApi.cs` і `Api/WaitersApi.cs`.

**Тестові дані:** кожен сценарій створює власну локацію, страви, два столи, окремого офіціанта, власника бронювання й ще одного клієнта (унікальні email). Бронювання створюється на `зараз + 3 дні`. Після сценарію бронювання скасовується, офіціант прибирається зі списку, локація, страви й столи видаляються. Тому сценарії не залежать один від одного й можуть виконуватися паралельно.

### Запуск

```bash
cp .env.example .env     # UI_URL, API_URL та обліковий запис офіціанта (див. нижче)
dotnet build
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=group-pre-order]"   # усі сценарії ДЗ
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=level1]"            # лише рівень 1 (level2, level3 аналогічно)
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=negative]"          # лише негативні
```

Для розгортання без AWS (сервер на Render) у `.env` вписуються:

```
API_URL=https://<ваш-сервіс>.onrender.com
UI_URL=https://<ваш-проєкт>.vercel.app
WAITER_USERNAME=waiter1@example.com
WAITER_PASSWORD=Password1!
ADMIN_USERNAME=admin@example.com
ADMIN_PASSWORD=Password1!
```

Звіти: `TestResults/` (TUnit HTML/TRX), `TestResults/reqnroll/reqnroll_report.html` (звіт Reqnroll), `TestResults/traces/` (Playwright traces для UI-сценаріїв, що впали). У CI (`aqa-tests.yml`) ці файли зберігаються як артефакти запуску, а впалий тест не зупиняє пайплайн.

### Обрана поведінка там, де ДЗ її не визначає

| Випадок | Поведінка |
|---|---|
| Не-Owner (інший клієнт, офіціант) | `403` `Only the reservation owner can perform this action.` |
| Неавторизований користувач або токен гостя замість токена клієнта | `401` від авторизатора |
| Групи / бронювання не існує | `404` |
| Повторне підтвердження | `400`, `errorCode: GroupReservationSubmitted`, `Group reservation was submitted.` |
| Немає підтверджених кошиків | `400` `There are no submitted guest sessions in this group.` |
| Підтверджені кошики без страв | `400` `Submitted guest sessions do not contain any dishes.` Через API цей стан недосяжний (порожній кошик не можна підтвердити, а підтверджений не можна змінити), тому сценарій позначено `@wip`; гілку покриває юніт-тест бекенду |
| Замовлення не знайдено | `400` `Order for reservation <id> was not found.` |
| Видалення останнього учасника | дозволено, повертається `participants: []`, `total: 0` |
| Учасник іншої групи | `400` `Guest session does not belong to this group reservation.` |
| Видалення власної сесії Owner | `400` `The owner session cannot be removed from the group.` |
| Група без учасників | `participants: []`, `total: 0` |
| Власна сесія Owner | створюється під час першого запиту `GET /reservations/{id}/guest-session-id`, далі повертається та сама; у списку учасників має `type: Owner` і не враховується в ліміті гостей |
| Після підтвердження | статус групи `Submitted`, нові гості приєднатися не можуть |

---

End-to-end tests for the Green & Tasty restaurant application: Reqnroll (Gherkin) + TUnit + Playwright for .NET, with the Screenplay pattern.

Guidance for extending the suite (writing Gherkin, steps, Screenplay, test data, working with an AI assistant) is in [CLAUDE.md](CLAUDE.md).

## Run the tests

```bash
cp .env.example .env        # then fill in your own values (see "Configuration")
dotnet build
dotnet run --no-build -- --report-trx                          # everything
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=smoke]"   # by tag
```

Everything a run produces lands in `TestResults/`: the TUnit report and TRX, the Reqnroll report in `reqnroll/`, and Playwright traces in `traces/` (open one with `npx playwright show-trace <file>.zip`).

Some scenarios are expected to fail (`@known-defect`, bugs in the application) or are skipped (`@wip`, no admin account configured). That is intentional.

## Quality gates

| Where | What runs |
|---|---|
| Git hooks (local) | `pre-commit`: `dotnet format` check and Gherkin lint on staged files · `commit-msg`: Conventional Commits · `pre-push`: build with analyzers |
| `aqa-quality.yml` | on every push and pull request: no generated files tracked, format, build (warnings are errors), Gherkin lint, test discovery |
| `aqa-tests.yml` | the scenarios themselves, nightly and on demand (Actions → AQA Tests → Run workflow) |

Enable the hooks once per clone, from the repository root:

```bash
dotnet tool restore --tool-manifest aqa/restaurant-aqa-framework/.config/dotnet-tools.json
git config core.hooksPath aqa/restaurant-aqa-framework/.husky
```

The hooks only act on commits and pushes that touch `aqa/restaurant-aqa-framework`. Gherkin lint needs Node.js.

## Configuration

There is no shared environment and no committed `.env`: copy `.env.example` to `.env` and fill it in with the data of **your own** deployment (see "Testing your own deployment"). `.env` is git-ignored; never commit it.

Settings come from `.env` or real environment variables (variables win):

| Setting | Meaning |
|---|---|
| `UI_URL` | where the frontend is served |
| `API_URL` | base URL of the backend API, including the stage (for example `…/develop`) |
| `WAITER_USERNAME`, `WAITER_PASSWORD` | a waiter account that already exists in the environment under test |
| `TESTTABLE_ID`, `TESTTABLE_WAITER_*` | a table and the waiter assigned to it (only for scenarios that need them) |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD` | an admin account; without it the admin scenarios are skipped |

The full list, with defaults, is in `.env.example`.

## Testing your own deployment

### If you deployed the backend / frontend / DevOps homework

1. `cp .env.example .env` and set `UI_URL` and `API_URL` to your deployment (API URL includes the stage, for example `…/develop`).
2. Waiters cannot be created by signing up, so create a waiter (and, for the admin scenarios, an admin) in *your* environment and put its credentials in `WAITER_*` / `ADMIN_*`. Optionally add a table and its assigned waiter (`TESTTABLE_*`).
3. Run `dotnet build` and a small slice first (`--treenode-filter "/*/*/*/*[Category=smoke]"`).

### If you have not deployed your own homework

If you deployed the original application, test that: set `UI_URL` / `API_URL` as above. Otherwise point the framework at a comparable public site (Booking, Airbnb, OpenTable, …) and write all the layers for it yourself: Gherkin features, step definitions, Screenplay tasks and questions, page objects with locators, and API clients where the site has a usable API. Use only flows you are allowed to automate (no purchases, no real bookings, respect the site's terms), and set `UI_URL` to that site.

1. **Generate a skeleton with Playwright codegen** (see "Presetup instructions"): record one flow, for example search → open a result → check details. Save the generated code to a scratch file, it is only raw material.
2. **Split it across the layers**: locators and small actions go to `Ui/` page objects, the flow becomes a task in `Screenplay/Tasks/`, the read-backs become questions in `Screenplay/Questions/`, the business wording goes to `Features/` and thin glue to `StepDefinitions/`.
3. **Replace weak locators.** Codegen often emits positional or text-fragile selectors. Prefer role + accessible name, label, test id, then CSS (see [CLAUDE.md](CLAUDE.md)); check them in the Playwright inspector.
4. Tag scenarios `@ui` / `@api`, keep them independent, and run `npx gherkin-lint -c .gherkin-lintrc "Features/*.feature"`.

### CI on your own public repository

Publish your work in your own public Git repository (the link is the deliverable). Workflows are disabled in a fresh fork until you enable them. Add the same values under Settings → Secrets and variables → Actions:

- **Variables:** `AQA_UI_URL`, `AQA_API_URL`, `AQA_TESTTABLE_ID`
- **Secrets:** `AQA_WAITER_USERNAME`, `AQA_WAITER_PASSWORD`, `AQA_ADMIN_USERNAME`, `AQA_ADMIN_PASSWORD`, `AQA_TESTTABLE_WAITER_USERNAME`, `AQA_TESTTABLE_WAITER_PASSWORD`

`aqa-quality.yml` runs on every push and pull request; `aqa-tests.yml` (nightly and on demand) tests whatever those variables point at. Never put credentials in the repository, in `.env.example` or in test code.

## Presetup instructions

This framework contains PlaywriteInstaller script so you don't need to setup it manually.
For full BDD highlighting in your IDE you may need to install Reqnroll extension https://docs.reqnroll.net/latest/ide-integrations/index.html
To start the Playwright code generator open Powershell in the framework directory and enter the line:
```bash
bin/Debug/net10.0/playwright.ps1 codegen "<your UI_URL or the public site you test>"
```
Build first so `playwright.ps1` exists. The URL is optional.
