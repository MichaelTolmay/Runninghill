# Runninghill word collection

Supported interface languages: English (ZA), Afrikaans (ZA), isiXhosa (ZA), isiZulu (ZA), and Setswana (ZA). See [language selection and CLI options](docs/starter-quick-guide.md#choose-your-language).

## For more information read the starter-quick-guide in the docs sub-directory!!

This is a dotnet 10 application, and will require it to be installed on the pc.
Python 3 is also a requirement to create an API key.

To generate an API key that is required run this, in the root folder of the project:
python3 scripts/dev-token.py (Windows python3 scripts\dev-token.py) The token will expire after 15 minutes.

Start with the [Starter quick guide](docs/starter-quick-guide.md) to run the website, API and included PostgreSQL database.

The [Photino desktop dashboard](docs/desktop-dashboard.md) monitors service and
website health, collection totals, response times under load, and application logs
on Windows, macOS and Linux, with the same five languages and company themes.

For your existing Alpine nginx container and shared web folder, use the [Alpine deployment guide](docs/deploy-alpine-nginx.md).

GitHub builds and tests run only after a pull request is **merged**, using that
merge's exact commit. Direct pushes, open PRs, and PRs closed without merging do
not start build jobs. The workflow runs the managed, tooling, CLI, browser,
database-provider, HTTPS, and outage tests, plus native publish and WinUI checks.
Its final **All builds and tests passed** check fails if any required job fails,
is cancelled, or is skipped. These checks run after merging, so they cannot block
the merge beforehand.

For authenticated CLI calls to local HTTPS, run [setup-cli.sh](scripts/setup-cli.sh) (Bash) or [setup-cli.ps1](scripts/setup-cli.ps1) (PowerShell). Each invocation generates a fresh token; pass a command such as `words list`, or omit arguments to check status. View this to see the URL's for the different services.

See [local HTTPS and nginx reverse proxy setup](docs/https.md) for the self-signed localhost certificate and HTTPS access on port 5443.

The Command Line Interpretor(CLI) uses https://localhost:5443, publish the CLI if missing, configure certificate trust, and generate a fresh token for every call. No arguments runs status.
Bash verified against the running service. PowerShell awaits Windows testing; it trusts the certificate in your current-user store. Instructions are in the [quick guide](Runninghill/docs/starter-quick-guide.md)

# Requirements

Write a full stack (frontend and back end) project for the following:
Make a web application that allows you to build a collection of
words based on their word types.
The types are: Noun, Verb, Adjective, Adverb, Pronoun,
Preposition, Interjection, Conjunction and Determiner.
The user must be able to create, read, update and delete words
from the collection.

The structure of the Word entity should look like this:
{
"id": 1,
"word": "evaluates",
"type": "Noun"
}

The restful GET call to retrieve the list of words needs to be made
to populate the lists on the frontend.
A restful POST call needs to be made to submit a new sentence.
The Words need to be persisted on the back end by a database of your
choosing, and the frontend must have a display of all the
previously created Words, as well as a mechanism to update and
delete them.

# Bonus points

● Create an application that can run on mobile devices as well.
● Using containerisation during development or hosting of the application
● Hosting the application on a cloud platform
● External logging and monitoring
● External config management
● DevOps pipeline for CI/CD flows
● Addition of complex architecture
