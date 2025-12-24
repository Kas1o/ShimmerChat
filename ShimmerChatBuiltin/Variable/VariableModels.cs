using System;

namespace ShimmerChatBuiltin.Variable
{
    public enum VariableScope
    {
        Agent,
        Chat
    }

    public enum VariableType
    {
        Float,
        Int,
        String
    }

    [Serializable]
    public class Variable
    {
        public string Name { get; set; } = string.Empty;
        public VariableType Type { get; set; }
        public VariableScope Scope { get; set; } = VariableScope.Chat;
        public string StringValue { get; set; } = string.Empty;
        public float FloatValue { get; set; }
        public int IntValue { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public object GetValue()
        {
            return Type switch
            {
                VariableType.Float => FloatValue,
                VariableType.Int => IntValue,
                VariableType.String => StringValue,
                _ => StringValue
            };
        }

        public void SetValue(object value)
        {
            UpdatedAt = DateTime.UtcNow;
            switch (Type)
            {
                case VariableType.Float:
                    FloatValue = Convert.ToSingle(value);
                    StringValue = FloatValue.ToString();
                    break;
                case VariableType.Int:
                    IntValue = Convert.ToInt32(value);
                    StringValue = IntValue.ToString();
                    break;
                case VariableType.String:
                    StringValue = value.ToString() ?? string.Empty;
                    break;
            }
        }

        public static Variable Create(string name, VariableType type, object value)
        {
            var variable = new Variable
            {
                Name = name,
                Type = type
            };
            variable.SetValue(value);
            return variable;
        }

        public string GetValueAsString()
        {
            return Type switch
            {
                VariableType.Float => FloatValue.ToString("G9"),
                VariableType.Int => IntValue.ToString(),
                VariableType.String => StringValue,
                _ => StringValue
            };
        }
    }

    [Serializable]
    public class VariableSet
    {
        public List<Variable> Variables { get; set; } = new List<Variable>();

        public Variable? GetVariable(string name)
        {
            return Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public void SetVariable(Variable variable)
        {
            var existing = GetVariable(variable.Name);
            if (existing != null)
            {
                var index = Variables.IndexOf(existing);
                Variables[index] = variable;
            }
            else
            {
                Variables.Add(variable);
            }
        }

        public void RemoveVariable(string name)
        {
            var variable = GetVariable(name);
            if (variable != null)
            {
                Variables.Remove(variable);
            }
        }

        public Dictionary<string, object> ToDictionary()
        {
            return Variables.ToDictionary(v => v.Name, v => v.GetValue(), StringComparer.OrdinalIgnoreCase);
        }

        public List<string> GetVariableNames()
        {
            return Variables.Select(v => v.Name).ToList();
        }
    }
}
