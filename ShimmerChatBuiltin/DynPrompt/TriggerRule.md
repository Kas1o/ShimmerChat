# TriggerRule

The `TriggerRule` property defines the conditions under which a dynamic prompt is activated. 
It is basically a expression composed of regex patterns and logical operators (`|`, `&`, `!`, `(`, `)`) that evaluate to true or false based on the input text.

## Syntax
- **Regex Patterns**: These are standard regular expressions that match specific text patterns.
- **Logical Operators**:
  - `|` (OR): At least one of the conditions must be true.
  - `&` (AND): All conditions must be true.
  - `!` (NOT): The condition must be false.
  - `(` and `)`: Used to group conditions and control the order of evaluation.


## Examples

```
"error" | "failure"
```
Triggers if the input text contains either "error" or "failure".

```
"success" & !"warning"
```
Triggers if the input text contains "success" and does not contain "warning".

```
("start" | "begin") & ("end" | "finish")
```
Triggers if the input text contains either "start" or "begin" and also contains either "end" or "finish".

```
"[0-9]" & !"[a-zA-Z]"
```
Triggers if the input text contains at least one digit and does not contain any letters.

## Escaping Special Characters
Like you saw in previous examples, these regex patterns are enclosed in double quotes.

To include special characters in your regex patterns, you may need to escape them using a backslash (`\`). For example, to match a literal dot (`.`), you would use `"\\."` in your pattern. first backslash is for literal escaping in the string, second backslash is for regex escaping.

In the implementation, We use the C# Newtonsoft.Json library to Parse those regex as string literals, so make sure to follow the JSON string escaping rules as well.