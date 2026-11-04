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

## Authorization credentials

The checked-in configuration leaves the Basic Authentication username and password blank. If enabling authorization, provide deployment-specific values through environment variables rather than committing credentials:

```powershell
$env:Security__Authorization__Username = "your-deployment-username"
$env:Security__Authorization__Password = "your-deployment-password"
$env:Security__Authorization__Enabled = "true"
```

Use unique credentials for each deployment, restrict CORS to trusted origins, and serve authenticated traffic over HTTPS.
