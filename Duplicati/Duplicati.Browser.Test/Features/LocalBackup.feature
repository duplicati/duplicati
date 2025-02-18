@Duplicati
Feature: Duplicati local backup
Link to a feature: [Calculator](Duplicati.Browser.Test/Features/LocalBackup.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

Scenario: Configure Backup
	Given there is local backup defined
	When you run the backup
	Then the result should be 120


Scenario Outline: Add two numbers permutations
	Given the first number is <First number>
	And the second number is <Second number>
	When the two numbers are added
	Then the result should be <Expected result>

Examples:
	| First number | Second number | Expected result |
	| 0            | 0             | 0               |
	| -1           | 10            | 9               |
	| 6            | 9             | 15              |