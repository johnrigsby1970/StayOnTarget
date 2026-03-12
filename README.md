Tool to manage personal budget, with envelopes mixed with known bills to project usage over longer term across multiple accounts and account types.

# StayOnTarget

Tool to manage personal budget, with envelopes mixed with known bills to project usage over longer term across multiple accounts and account types.

## Dependencies

* This program is coded to run on Windows 10 and above using Microsoft .Net 8 runtime. 
* You will need to install the Microsoft .Net Desktop Runtime.
* Microsoft .NET Desktop Runtime can be found at https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Description

The application allocates money from accounts into named buckets/categories (envelopes) and tracks expected vs. actual amounts per period, which is the core concept of envelope budgeting.

1. **Budget Bucket** - represents named budget categories with expected amounts, which are the "envelopes"
2. **Period Bucket** - tracks actual amounts per period for each bucket, allowing you to allocate money into envelopes each pay period
3. **Account linkage** - buckets can be linked to specific accounts, enabling envelope-style allocation of funds

## Getting Started

### Installing

* Using the green <> Code button in GitHub, download the zip file of this repository.
* Download and extract the zip file to a directory on your computer. The location is not important, but you need to 
  remember where you put it.
* Build it yourself or execute the published file in the folder StayOnTarget\bin\Release\net8.0-windows\publish.
* Locate and run the StayOnTarget.exe program.
* You will need the .net 8 desktop runtime installed. If prompted, download and install the runtime.
* Windows will warn you "Don't Run!" (explained below) the first time you execute the program. Choose the "More info" link to show the "Run anyway" button.
* Click this "Run anyway" button.


### Executing program

* Execute StayOnTarget.exe. As mentioned above, you will be told the program should not be trusted. This program is not signed with 
a code signing certificate. Trust it or not, but you can also inspect and compile the code yourself from this 
repository if you do not want to trust the compiled program. 100% of the code is open source and available.
* The reason for the prompt is that the compiled code is not signed with a certificate. What that means to you is that it is  
 possible a hacker could alter it after it is built and before you download it. If it is signed, a hacker cannot do that. 
* The program is built and directly uploaded to GitHub. Unless the hacker can break in to GitHub, the code is safe.
* A certificate costs $600 per year. This software is currently free to use. One that is signed would cost money, and at present, the use audience doesn't justify the cost. 

## Authors

John Rigsby

## Version History


## License

Stay On Target  © 2026 by John Rigsby is licensed under CC BY-NC-ND 4.0. To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0/

## Acknowledgments


