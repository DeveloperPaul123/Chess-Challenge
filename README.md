# Leonidas Chess Bot

This repository contains my entry for the [Chess Coding Challenge](https://youtu.be/Ne40a5LkK6A?si=urkm1MWbZ6jLIwZt) by Sebatian Lague. The bot finished [20th](https://github.com/SebLague/Tiny-Chess-Bot-Challenge-Results/blob/fcebda936c5882efdeb61382592bda77f2c1e6c0/RatingsList.txt#L25)~~in the top 32 of all entries (600+)~~!

The original challenge rules are below.

# Chess Coding Challenge (C#)

Welcome to the [chess coding challenge](https://youtu.be/iScy18pVR58)! This is a friendly competition in which your goal is to create a small chess bot (in C#) using the framework provided in this repository.
Once submissions close, these bots will battle it out to discover which bot is best!

I will then create a video exploring the implementations of the best and most unique/interesting bots.
I also plan to make a small game that features these most interesting/challenging entries, so that everyone can try playing against them.

## Rules

* You may participate alone, or in a group of any size.
* You may submit a maximum of two entries.
  * Please only submit a second entry if it is significantly different from your first bot (not just a minor tweak).
  * Note: you will need to log in with a second Google account if you want submit a second entry.
* Only the following namespaces are allowed:
  * `ChessChallenge.API`
  * `System`
  * `System.Numerics`
  * `System.Collections.Generic`
  * `System.Linq`
    * You may not use the `AsParallel()` function
* As implied by the allowed namespaces, you may not read data from a file or access the internet, nor may you create any new threads or tasks to run code in parallel/in the background.
* You may not use the unsafe keyword.
* You may not store data inside the name of a variable/function/class etc (to be extracted with `nameof()`, `GetType().ToString()`, `Environment.StackTrace` and so on). Thank you to [#12](https://github.com/SebLague/Chess-Challenge/issues/12) and [#24](https://github.com/SebLague/Chess-Challenge/issues/24).
* If your bot makes an illegal move or runs out of time, it will lose the game.
  * Games are played with 1 minute per side by default (this can be changed in the settings class). The final tournament time control is TBD, so your bot should not assume a particular time control, and instead respect the amount of time left on the timer (given in the Think function).
* Your bot may not use more than 256mb of memory for creating look-up tables (such as a transposition table).
* If you have added a constructor to MyBot (for generating look up tables, etc.) it may not take longer than 5 seconds to complete.
* All of your code/data must be contained within the _MyBot.cs_ file.
  * Note: you may create additional scripts for testing/training your bot, but only the _MyBot.cs_ file will be submitted, so it must be able to run without them.
  * You may not rename the _MyBot_ struct or _Think_ function contained in the _MyBot.cs_ file.
  * The code in MyBot.cs may not exceed the _bot brain capacity_ of 1024 (see below).

## Bot Brain Capacity

There is a size limit on the code you create called the _bot brain capacity_. This is measured in ‘tokens’ and may not exceed 1024. The number of tokens you have used so far is displayed on the bottom of the screen when running the program.

All names (variables, functions, etc.) are counted as a single token, regardless of length. This means that both lines of code: `bool a = true;` and `bool myObscenelyLongVariableName = true;` count the same. Additionally, the following things do not count towards the limit: white space, new lines, comments, access modifiers, commas, and semicolons.
