# Server-side pivot-engine for pivot table

Introduced a server-side engine where all the pivot calculations, filtering, sorting, etc. are done. Then, the information to be displayed in the viewport will be passed to the client side. This prevents transferring the entire data source to a browser, which reduces network traffic and increases rendering performance of the Pivot Table especially where there are millions of data. It works best when virtual scrolling is enabled. The engine supports different kinds of data sources such as JSON, CSV, DataTable, and collections.

## OpenSpec workflow

Bug fixes and feature work use OpenSpec before code changes. The project schema is `bugfix-feature`.

Start a change with the Code Studio command `/opsx-propose`, or from a shell:

```bash
openspec new change "<kebab-case-name>"
openspec status --change "<kebab-case-name>"
```

`openspec new change` writes `.openspec.yaml`. The schema then requires these artifacts before implementation:

- `EXPLORATION.md`
- `proposal.md`
- `specs/<capability>/spec.md`
- `design.md`
- `tasks.md`

Implement only after those files exist, using `/opsx-apply`. Archive a completed change with `/opsx-archive`.

The following steps demonstrate how to run the application,
* Open the [PivotController](./PivotController/) application in Visual Studio.
* Dependent packages will be downloaded automatically from the nuget.org site.
* Run the application once the packages are downloaded.
* Once the application is hosted in the localhost, copy and paste its URL in the **Sample->pivot.js** file.
* Then open the **Sample->pivot.html** file. The pivot table will be populated in the browser.


 



 

## Testing credentials (D6 credential-hygiene)

`PivotController/appsettings.json` ships **without** any username/password
values (`Security:Authorization:Username/Password` are empty, so Basic Auth is
inactive by default — the middleware requires both `Enabled: true` **and** a
non-empty username).

To test with Basic Authorization enabled, supply the credential via
user-secrets or an environment variable — never by editing the tracked
`appsettings.json`:

```bash
cd PivotController
dotnet user-secrets init
dotnet user-secrets set "Security:Authorization:Username" "admin"
dotnet user-secrets set "Security:Authorization:Password" "<your-test-password>"
dotnet run
```

Or with environment variables:

```bash
export Security__Authorization__Username=admin
export Security__Authorization__Password='<your-test-password>'
dotnet run
```

If you previously ran with the old test credential committed to history, rotate
it before reuse (documented limitation: history rewrite is out of scope for
this change).
