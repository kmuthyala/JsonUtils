﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using JsonUtils.Parser.Exceptions;

namespace JsonUtils.Parser
{
  public class JsonParser
  {
    private readonly TextReader _reader;
    private char _char;
    private bool _canRead;
    private short _line = 1;
    private short _lineDecrement;
    private bool _skipSpace = true;
    private bool _insideObject;
    // per Json Specification, only certain chars are valid after colon
    private char[] _validCharsAfterColon = { '"', '{', '[', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 't', 'f', 'n' }; // for when inside object
    private char[] _braceAndQuotesAfterColon = { '"', '{', '[' }; // for when not inside object

    public JsonParser(Stream jsonStream)
    {
      _reader = new StreamReader(jsonStream);
      ReadNext();
    }
    public JsonParser(string jsonString)
    {
      _reader = new StringReader(jsonString);
      ReadNext();
    }

    public JsonResult Parse()
    {
      return ReadValue();
    }

    private JsonObject ReadObject(string keyy)
    {
      var obj = new JsonObject { IsAtLine = _line, Type = JsonObjectType.Object };
      _insideObject = true;
      while (true)
      {
        ReadNext();

        if (_char == '}') // if empty object
        {
          _insideObject = false;
          break;
        }

        var key = ReadKey(); // gets key
        CheckForValidCharacters();
        var value = ReadValue(key);

        if (obj.ContainsKey(key))
          obj[key].DuplicatesAt.Add(_line);
        else
          obj.Add(key, value);

        if (value.Type != JsonObjectType.Number && value.Type != JsonObjectType.Null && value.Type != JsonObjectType.Boolean)
          ReadNext();

        if (_char == ',')
          continue;

        if (_char == '}')
        {
          _insideObject = false;
          break;
        }

        throw new InvalidJsonException(_line);
      }
      if (keyy != null)
      {
        obj.Key = keyy;
        obj.Type = JsonObjectType.Object;
      }
      return obj;
    }
    private JsonArray ReadArray(string key)
    {
      var list = new JsonArray()
      {
        Type = JsonObjectType.Array,
        Key = key,
        IsAtLine = _line
      };
      ReadNext();

      while (true)
      {
        var val = ReadValue();
        list.Add(val);
        if (val.Type == JsonObjectType.Object || val.Type == JsonObjectType.Array || val.Type == JsonObjectType.String)
          ReadNext();
        if (_char != ',')
        {
          break;
        }
        ReadNext();
      }
      if (_char != ']')
        throw new InvalidJsonException(_line);

      return list;
    }
    private string ReadKey()
    {
      if (_char == '"')
        return ReadString();

      throw new InvalidJsonException(_line);
    }
    private JsonResult ReadValue(string key = null)
    {
      if (_canRead)
      {
        // according to JSON specification, following value can be object,array,number,true,false or null (http://json.org)
        switch (_char)
        {
          case '{':
            return ReadObject(key);
          case '[':
            return ReadArray(key);
          case '"':
          case 't':
          case 'f':
          case 'n':
            return ReadNonNumeric(key);
          default:
            return ReadNumeric(key);
        }
      }
      throw new InvalidJsonException(_line);
    }
    private JsonObject ReadNonNumeric(string key)
    {
      switch (_char)
      {
        case '"':
          return ReadStringByKey(key);
        case 't':
        case 'f':
        case 'n':
          return ReadBoolOrNull(key);
        default:
          throw new InvalidJsonException(_line);
      }
    }
    private JsonObject ReadStringByKey(string key)
    {
      return new JsonObject
      {
        Key = key,
        Value = ReadString(skipSpace: false),
        Type = JsonObjectType.String,
        IsAtLine = _line
      };
    }
    private JsonObject ReadNumeric(string key)
    {
      var sb = new StringBuilder();
      _lineDecrement = 0;
      while (true)
      {
        if (_char == ',' || _char == '}' || _char == ']')
          break;

        sb.Append(_char);
        ReadNext();
      }
      var retVal = sb.ToString().Trim();
      if (retVal == "" ||
          (retVal != "" && Regex.IsMatch(sb.ToString(), @"^(-)?[0-9]*(\.[0-9]+)*([eE][-+]?[0-9]+)*$")))
      {
        return new JsonObject
        {
          Key = key,
          Value = GetNumeric(retVal),
          Type = JsonObjectType.Number,
          IsAtLine = _line
        };
      }
      var line = (short)(_line - _lineDecrement);
      throw new InvalidJsonException(line);
    }
    private JsonObject ReadBoolOrNull(string key)
    {
      var sb = new StringBuilder();
      while (true)
      {
        if (_char == ',' || _char == '}' || _char == ']')
          break;

        sb.Append(_char);
        ReadNext();
      }
      var retVal = sb.ToString().Trim();
      if (retVal == "true" || retVal == "false")
      {
        return new JsonObject
        {
          Key = key,
          Value = bool.Parse(retVal),
          Type = JsonObjectType.Boolean,
          IsAtLine = _line
        };
      }
      else if (retVal == "null")
      {
        return new JsonObject
        {
          Key = key,
          Value = null,
          Type = JsonObjectType.Null
        };
      }
      throw new InvalidJsonException(_line);
    }
    private string ReadString(bool skipSpace = true)
    {
      _skipSpace = skipSpace;
      var sb = new StringBuilder();
      while (true)
      {
        ReadNext();

        if (_char == '"')
          break;

        sb.Append(_char != '\\' ? _char : GetEscapeChar());
      }
      _skipSpace = true;
      return sb.ToString();
    }
    private object GetNumeric(string s)
    {
      if (!int.TryParse(s, out int intValResult))
      {
        decimal.TryParse(s, out decimal decValResult);
        return decValResult;
      }
      return intValResult;
    }
    private char GetEscapeChar()
    {
      // verify if it is a valid escape character
      // according to JSON specification, following characters are allowed  (http://json.org)
      ReadNext(); // check the next character

      switch (_char)
      {
        case '"':
        case '\\':
        case '/':
          return _char;
        case 'b':
          return '\b';
        case 'f':
          return '\f';
        case 'n':
          return '\n';
        case 'r':
          return '\r';
        case 't':
          return '\t';
        case 'u':
          return GetUnicode();
        default:
          throw new InvalidJsonException(_line);
      }
    }
    private char GetUnicode()
    {
      var sb = new StringBuilder();
      int i = 1;
      while (i <= 4)
      {
        ReadNext();
        if (_canRead)
          sb.Append(_char);
        else
          throw new InvalidJsonException(_line);
        i++;
      }
      var ucodestring = sb.ToString();
      if (Regex.IsMatch(ucodestring, "^[0-9a-fA-F]{4}$"))
      {
        return Convert.ToChar(System.Convert.ToUInt16(ucodestring, 16));
      }
      throw new InvalidJsonException(_line);
    }
    private void ReadNext()
    {
      ReadChar();

      // if the character read is one of the following, skip
      var ignorable = new HashSet<char>() { '\r', '\n', '\t' };
      while (true)
      {
        if (ignorable.Any(i => i == _char) || (_skipSpace && _char == ' '))
        {
          ReadChar();
          continue;
        }
        return;
      }
    }
    private void ReadChar()
    {
      _canRead = _reader.Peek() >= 0;
      _char = (char)_reader.Read();
      if (_char == '\n')
      {
        _line++;
        _lineDecrement++;
      }
    }
    private void CheckForValidCharacters()
    {
      ReadNext();

      if (_char != ':')
        throw new InvalidJsonException(_line);

      ReadNext();

      if (_insideObject)
      {
        if (_validCharsAfterColon.All(c => _char != c))
          throw new InvalidJsonException(_line);
      }
      else
      {
        if (_braceAndQuotesAfterColon.All(c => _char != c))
          throw new InvalidJsonException(_line);
      }
    }

  }

  public enum JsonObjectType
  {
    String = 0,
    Number = 1,
    Object = 2,
    Array = 3,
    Boolean = 4,
    Null = 5,
  }
}


