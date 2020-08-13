_This bot was migrated from python to dotnet core. Original source code: https://github.com/nateyolles/slack-pokerbot_

# Slack Pokerbot for AWS Lambda (in dotnet core!)

Pokerbot is a [Slash Command](https://api.slack.com/slash-commands) for [Slack](https://slack.com/). It's easily hosted on [Amazon Web Services' Lambda](https://aws.amazon.com/lambda/).

![Screenshot of Pokerbot in Slack](https://raw.githubusercontent.com/nateyolles/slack-pokerbot/master/images/screenshot.png)

## Configure Slack Slash Command

1. Navigate to https://<your-team-name>.slack.com/apps/manage/custom-integrations
2. Click on "Slash Commands" and "Add Configuration" 
3. Set the Command to "/poker"
4. Set the URL to the path provided by AWS
5. Set the Method to "POST"
6. Set Custom Name to "poker"
7. Customize Icon if you wish
8. Check "Show this command in the autocomplete list"
9. Set Description to "Play Scrum planning poker"
10. Set Usage Hint to "help [or deal, vote <number>, tally, reveal]""
11. Copy the Token

## Configure

1. Paste the Slack Token
2. Set the path to your images
3. Set the planning poker values you want to use (e.g. 0, 1, 2, 3, 5, 8, 13, 20, 40, 100)

## Play Poker Planning
1. Type "/poker deal" in a channel
2. Everyone votes by typing "/poker vote <your vote>"
3. Type "/pokerbot tally" in the channel to show the names of those who have voted
4. Type "/pokerbot reveal" in the channel to reveal the voting results

## Here are some steps to follow from Visual Studio:

To deploy your Serverless application, right click the project in Solution Explorer and select *Publish to AWS Lambda*.

To view your deployed application open the Stack View window by double-clicking the stack name shown beneath the AWS CloudFormation node in the AWS Explorer tree. The Stack View also displays the root URL to your published application.

## Here are some steps to follow to get started from the command line:

Once you have edited your template and code you can deploy your application using the [Amazon.Lambda.Tools Global Tool](https://github.com/aws/aws-extensions-for-dotnet-cli#aws-lambda-amazonlambdatools) from the command line.

Install Amazon.Lambda.Tools Global Tools if not already installed.
```
    dotnet tool install -g Amazon.Lambda.Tools
```

If already installed check if new version is available.
```
    dotnet tool update -g Amazon.Lambda.Tools
```

Deploy application
```
    dotnet lambda deploy-serverless -cfg aws-lambda-tools-defaults.mine.json
```
