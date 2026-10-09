# Runninghill word collection

This is a dotnet 10 application, and will require it to be installed on the pc.
Python 3 is also a requirement to create an API key.

To generate an API key that is required run this, in the root folder of the project:
python3 scripts/dev-token.py (Windows python3 scripts\dev-token.py) keep the token as you will need it, everytime you come back to the application.

Start with the [Starter quick guide](docs/starter-quick-guide.md) to run the website, API and included PostgreSQL database.

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
