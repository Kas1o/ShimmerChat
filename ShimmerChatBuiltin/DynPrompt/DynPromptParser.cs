using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ShimmerChatBuiltin.DynPrompt
{
    /// <summary>
    /// Token类型枚举
    /// </summary>
    public enum TokenType
    {
        LeftParen,    // ( 左括号
        RightParen,   // ) 右括号
        Not,          // ! 非操作符
        And,          // & 与操作符
        Or,           // | 或操作符
        String,       // "..." 字符串（正则表达式模式）
        EOF           // 文件结束标记
    }

    /// <summary>
    /// Token类，表示词法单元
    /// </summary>
    public class Token
    {
        public TokenType Type { get; }
        public string Lexeme { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenType type, string lexeme, int line, int column)
        {
            Type = type;
            Lexeme = lexeme;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"{Type}: '{Lexeme}' at line {Line}, column {Column}";
        }
    }

    /// <summary>
    /// 抽象语法树节点基类
    /// </summary>
    public abstract class Expression
    {
        public abstract bool Evaluate(string context);
    }

    /// <summary>
    /// 正则表达式节点
    /// </summary>
    public class RegexExpression : Expression
    {
        public string Pattern { get; }
        private Regex _compiledRegex;

        public RegexExpression(string pattern)
        {
            Pattern = pattern;
            try
            {
                _compiledRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid regex pattern: {pattern}", ex);
            }
        }

        public override bool Evaluate(string context)
        {
            return _compiledRegex.IsMatch(context);
        }
    }

    /// <summary>
    /// 非操作节点
    /// </summary>
    public class NotExpression : Expression
    {
        public Expression Operand { get; }

        public NotExpression(Expression operand)
        {
            Operand = operand;
        }

        public override bool Evaluate(string context)
        {
            return !Operand.Evaluate(context);
        }
    }

    /// <summary>
    /// 二元操作节点基类
    /// </summary>
    public abstract class BinaryExpression : Expression
    {
        public Expression Left { get; }
        public Expression Right { get; }

        protected BinaryExpression(Expression left, Expression right)
        {
            Left = left;
            Right = right;
        }
    }

    /// <summary>
    /// 与操作节点
    /// </summary>
    public class AndExpression : BinaryExpression
    {
        public AndExpression(Expression left, Expression right) : base(left, right) { }

        public override bool Evaluate(string context)
        {
            return Left.Evaluate(context) && Right.Evaluate(context);
        }
    }

    /// <summary>
    /// 或操作节点
    /// </summary>
    public class OrExpression : BinaryExpression
    {
        public OrExpression(Expression left, Expression right) : base(left, right) { }

        public override bool Evaluate(string context)
        {
            return Left.Evaluate(context) || Right.Evaluate(context);
        }
    }

    /// <summary>
    /// 词法分析器
    /// </summary>
    public class Lexer
    {
        private readonly string _source;
        private readonly int _length;
        private int _position = 0;
        private int _line = 1;
        private int _column = 1;

        public Lexer(string source)
        {
            _source = source ?? string.Empty;
            _length = _source.Length;
        }

        /// <summary>
        /// 获取下一个Token
        /// </summary>
        public Token NextToken()
        {
            // 跳过空白字符
            SkipWhitespace();

            // 检查是否到达文件末尾
            if (_position >= _length)
            {
                return new Token(TokenType.EOF, string.Empty, _line, _column);
            }

            char current = _source[_position];

            // 处理单字符Token
            switch (current)
            {
                case '(': 
                    Advance(); 
                    return new Token(TokenType.LeftParen, "(", _line, _column - 1);
                case ')': 
                    Advance(); 
                    return new Token(TokenType.RightParen, ")", _line, _column - 1);
                case '!': 
                    Advance(); 
                    return new Token(TokenType.Not, "!", _line, _column - 1);
                case '&': 
                    Advance(); 
                    return new Token(TokenType.And, "&", _line, _column - 1);
                case '|': 
                    Advance(); 
                    return new Token(TokenType.Or, "|", _line, _column - 1);
                case '"':
                    return ScanString();
                default:
                    throw new ArgumentException($"Unexpected character '{current}' at line {_line}, column {_column}");
            }
        }

        /// <summary>
        /// 前进到下一个字符
        /// </summary>
        private void Advance()
        {
            if (_position < _length)
            {
                if (_source[_position] == '\n')
                {
                    _line++;
                    _column = 1;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
        }

        /// <summary>
        /// 查看下一个字符但不前进
        /// </summary>
        private char Peek()
        {
            if (_position < _length)
            {
                return _source[_position];
            }
            return '\0';
        }

        /// <summary>
        /// 跳过空白字符
        /// </summary>
        private void SkipWhitespace()
        {
            while (_position < _length && char.IsWhiteSpace(_source[_position]))
            {
                Advance();
            }
        }

        /// <summary>
        /// 扫描字符串Token
        /// </summary>
        private Token ScanString()
        {
            int startLine = _line;
            int startColumn = _column;
            StringBuilder sb = new StringBuilder();

            // 跳过开始的引号
            Advance();

            bool isEscaped = false;

            while (_position < _length)
            {
                char current = _source[_position];

                if (isEscaped)
                {
                    // 处理转义字符
                    switch (current)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(current); break;
                    }
                    isEscaped = false;
                }
                else if (current == '\\')
                {
                    isEscaped = true;
                }
                else if (current == '"')
                {
                    // 找到字符串结束
                    Advance();
                    return new Token(TokenType.String, sb.ToString(), startLine, startColumn);
                }
                else
                {
                    sb.Append(current);
                }

                Advance();
            }

            // 未找到匹配的结束引号
            throw new ArgumentException($"Unterminated string starting at line {startLine}, column {startColumn}");
        }
    }

    /// <summary>
    /// 语法分析器
    /// </summary>
    public class Parser
    {
        private readonly Lexer _lexer;
        private Token _currentToken;

        public Parser(Lexer lexer)
        {
            _lexer = lexer;
            _currentToken = _lexer.NextToken();
        }

        /// <summary>
        /// 解析表达式
        /// </summary>
        public Expression Parse()
        {
            Expression expr = ParseExpression();
            
            // 确保解析完所有Token
            if (_currentToken.Type != TokenType.EOF)
            {
                throw new ArgumentException($"Unexpected token {_currentToken} at line {_currentToken.Line}, column {_currentToken.Column}");
            }
            
            return expr;
        }

        /// <summary>
        /// 解析表达式（处理OR操作符）
        /// </summary>
        private Expression ParseExpression()
        {
            Expression expr = ParseTerm();

            while (_currentToken.Type == TokenType.Or)
            {
                Consume(TokenType.Or);
                Expression right = ParseTerm();
                expr = new OrExpression(expr, right);
            }

            return expr;
        }

        /// <summary>
        /// 解析项（处理AND操作符）
        /// </summary>
        private Expression ParseTerm()
        {
            Expression expr = ParseFactor();

            while (_currentToken.Type == TokenType.And)
            {
                Consume(TokenType.And);
                Expression right = ParseFactor();
                expr = new AndExpression(expr, right);
            }

            return expr;
        }

        /// <summary>
        /// 解析因子（处理NOT操作符和括号）
        /// </summary>
        private Expression ParseFactor()
        {
            // 处理NOT操作符
            if (_currentToken.Type == TokenType.Not)
            {
                Consume(TokenType.Not);
                Expression expr = ParseFactor();
                return new NotExpression(expr);
            }

            // 处理括号
            if (_currentToken.Type == TokenType.LeftParen)
            {
                Consume(TokenType.LeftParen);
                Expression expr = ParseExpression();
                Consume(TokenType.RightParen);
                return expr;
            }

            // 处理字符串（正则表达式）
            if (_currentToken.Type == TokenType.String)
            {
                string pattern = _currentToken.Lexeme;
                Consume(TokenType.String);
                return new RegexExpression(pattern);
            }

            throw new ArgumentException($"Unexpected token {_currentToken} at line {_currentToken.Line}, column {_currentToken.Column}");
        }

        /// <summary>
        /// 消费当前Token并前进到下一个
        /// </summary>
        private void Consume(TokenType expectedType)
        {
            if (_currentToken.Type == expectedType)
            {
                _currentToken = _lexer.NextToken();
            }
            else
            {
                throw new ArgumentException($"Expected token of type {expectedType}, but found {_currentToken.Type} at line {_currentToken.Line}, column {_currentToken.Column}");
            }
        }
    }

    /// <summary>
    /// DynPrompt表达式评估器
    /// </summary>
    public static class DynPromptEvaluator
    {
        /// <summary>
        /// 评估表达式
        /// </summary>
        public static bool Evaluate(string expression, string context)
        {
            if (string.IsNullOrEmpty(expression))
            {
                return true; // 空表达式默认为true
            }

            if (context == null)
            {
                context = string.Empty;
            }

            try
            {
                Lexer lexer = new Lexer(expression);
                Parser parser = new Parser(lexer);
                Expression expr = parser.Parse();
                return expr.Evaluate(context);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Unexpected character") || 
                                              ex.Message.Contains("Unterminated string") ||
                                              ex.Message.Contains("Unexpected token"))
            {
                // 语法错误，更详细的错误信息
                throw new Exception($"Syntax error in expression '{expression}': {ex.Message}");
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
				// 正则表达式错误
				throw new Exception($"Invalid regex pattern in expression '{expression}': {ex.Message}");
            }
            catch (Exception ex)
            {
				// 其他评估错误
				throw new Exception($"Error evaluating expression '{expression}': {ex.Message}");
            }
        }
    }
}