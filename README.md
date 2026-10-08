# Runninghill word collection

See [database configuration and EF Core 10 migrations](docs/databases.md) for PostgreSQL, MSSQL, SQLite and MySQL.

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
