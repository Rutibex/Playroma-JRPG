var $plugins =
[
  {
    "name": "PlayromaFixtureParameters",
    "status": true,
    "description": "Synthetic parameter encoding fixture",
    "parameters": {
      "BooleanLike": "true",
      "IntegerLike": "42",
      "FloatLike": "3.14159",
      "ArrayString": "[\"A\",\"B\",\"C\"]",
      "ObjectString": "{\"enabled\":true,\"count\":2}",
      "StructArray": "[\"{\\\"name\\\":\\\"One\\\",\\\"value\\\":\\\"1\\\"}\",\"{\\\"name\\\":\\\"Two\\\",\\\"value\\\":\\\"2\\\"}\"]",
      "Multiline": "Line one\\nLine two",
      "Filename": "FixtureAsset",
      "Unicode": "Café — 日本語 — 🐈",
      "EscapedQuote": "He said \\\"Hello\\\".",
      "Empty": ""
    }
  },
  {
    "name": "PlayromaFixtureParameters",
    "status": false,
    "description": "Deliberate duplicate entry for preservation tests",
    "parameters": {
      "BooleanLike": "false"
    }
  }
];
