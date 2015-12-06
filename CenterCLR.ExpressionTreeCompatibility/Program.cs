////////////////////////////////////////////////////////////////////////////////////////////////////
//
// CenterCLR.ExpressionTreeCompatibility
// Copyright (c) Kouji Matsui, All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
// * Redistributions of source code must retain the above copyright notice,
//   this list of conditions and the following disclaimer.
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
// IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
// INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
// HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
// EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Linq.Expressions;

namespace CenterCLR.ExpressionTreeCompatibility
{
	class Program
	{
		// ラムダ式からデリゲートの生成
		static void LambdaToDelegate()
		{
			// 普通のラムダ式をデリゲートとして保持
			Func<int, string> stringFunc = value => string.Format("Value = {0}", value);

			// 例：デリゲートを実行
			var result = stringFunc(123);
			Debug.Assert(result == "Value = 123");
		}

		// ラムダ式から式木の生成
		static void LambdaToExpression()
		{
			// ラムダ式を式木として保持
			Expression<Func<int, string>> stringExpr = value => string.Format("Value = {0}", value);

			// 例：ラムダ式の構造を調べる
			Debug.Assert(stringExpr.Parameters[0].Name == "value");
			Debug.Assert(stringExpr.Parameters[0].Type == typeof(int));
			Debug.Assert(stringExpr.ReturnType == typeof(string));
			var mcExpr = stringExpr.Body as MethodCallExpression;
			Debug.Assert(mcExpr.Method.DeclaringType == typeof(string));
			Debug.Assert(mcExpr.Method.Name == "Format");
			Debug.Assert(mcExpr.Arguments[0].Type == typeof(string));

			// 式木を動的にコンパイルする
			Func<int, string> compiledFunc = stringExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == "Value = 123");
		}

		// 動的に式木を生成
		static void DynamicConstruction()
		{
			// 式木を動的に構築する

			// ラムダ式のパラメータに相当する式木
			var pValueExpr = Expression.Parameter(typeof(int), "value");

			// string.FormatメソッドのMethodInfo
			var stringFormatMethod = typeof(string).
				GetMethod("Format", new[] { typeof(string), typeof(object) });

			// ラムダ式の本体に相当する式木 「string.Format("Value = {0}", value)」
			// Convertが必要なのは、引数の型はobjectなのでintから変換が必要なため（暗黙変換が行われない）
			var bodyExpr = Expression.Call(
				stringFormatMethod,
				Expression.Constant("Value = {0}"),
				Expression.Convert(pValueExpr, typeof(object)));

			// ラムダ式に相当する式木 「value => string.Format("Value = {0}", value)」
			Expression<Func<int, string>> dynamicStringExpr =
				Expression.Lambda<Func<int, string>>(
					bodyExpr,
					pValueExpr);

			// 式木を動的にコンパイルする
			Func<int, string> compiledFunc = dynamicStringExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == "Value = 123");
		}

		#region ExpressionAssignForField
		public static int _fieldValue;

		// Expression.Assignを使ってフィールド代入
		static void ExpressionAssignForField()
		{
#if !NET35
			_fieldValue = -1;

			// 代入式の概念（C#ソースコードとしてコンパイルは不可）
			// Expression<Func<int, int>> assignExpr = value => _fieldValue = value;

			// 動的に式木を構築すれば可能

			// パラメータの式木
			var pValueExpr = Expression.Parameter(typeof(int), "value");

			// フィールドを示す式木
			var fieldValueExpr = Expression.Field(null, typeof(Program).GetField("_fieldValue"));

			// Expression.Assignを使って代入式を示す式木を作り、それをbodyにラムダ式木を生成
			Expression<Func<int, int>> assignExpr =
				Expression.Lambda<Func<int, int>>(
					Expression.Assign(fieldValueExpr, pValueExpr),
					pValueExpr);

			var assignmentExpr = assignExpr.Body as BinaryExpression;
			Debug.Assert(assignmentExpr.Type == typeof(int));
			var fieldExpr = assignmentExpr.Left as MemberExpression;
			Debug.Assert(fieldExpr.Member.Name == "_fieldValue");
			Debug.Assert(fieldExpr.Type == typeof(int));
			var valueExpr = assignmentExpr.Right as ParameterExpression;
			Debug.Assert(valueExpr.Name == "value");
			Debug.Assert(valueExpr.Type == typeof(int));

			// 式木を動的にコンパイルする
			Func<int, int> compiledFunc = assignExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == 123);
			Debug.Assert(_fieldValue == 123);
#endif
		}

		// フィールドに値を代入させるためのヘルパーメソッド
		public static TValue FieldSetter<TValue>(out TValue field, TValue newValue)
		{
			field = newValue;	// outなので、対象に代入される
			return newValue;	// ちゃんと値を返すことで、式の体を成す
		}

		// Expression.Assignを使わずにフィールド代入
		static void ExpressionAssignForFieldCompatible()
		{
			_fieldValue = -1;

			// パラメータの式木
			var pValueExpr = Expression.Parameter(typeof(int), "value");

			// フィールドを示す式木
			var fieldValueExpr = Expression.Field(null, typeof(Program).GetField("_fieldValue"));
			
			// FieldSetterのクローズジェネリックメソッド
			var fieldSetterMethod = typeof(Program).
				GetMethod("FieldSetter").
				MakeGenericMethod(fieldValueExpr.Type);

			// FieldSetterを呼び出す式木から、ラムダ式木を生成
			var assignExpr =
				Expression.Lambda<Func<int, int>>(
					Expression.Call(fieldSetterMethod,
						fieldValueExpr, 	// outでもうまいことやってくれる
						pValueExpr),
					pValueExpr);

			// 式木を動的にコンパイルする
			Func<int, int> compiledFunc = assignExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == 123);
			Debug.Assert(_fieldValue == 123);
		}
		#endregion

		#region ExpressionAssignForProperty
		public static int TestProperty { get; set; }

		// Expression.Assignを使ってプロパティ代入
		static void ExpressionAssignForProperty()
		{
#if !NET35
			TestProperty = -1;

			// 代入式の概念（C#ソースコードとしてコンパイルは不可）
			// Expression<Func<int, int>> assignExpr = value => TestProperty = value;

			// 動的に式木を構築すれば可能

			// パラメータの式木
			var pValueExpr = Expression.Parameter(typeof(int), "value");

			// プロパティを示す式木
			var testPropertyExpr = Expression.Property(null, typeof(Program).GetProperty("TestProperty"));

			// Expression.Assignを使って代入式を示す式木を作り、それをbodyにラムダ式木を生成
			Expression<Func<int, int>> assignExpr =
				Expression.Lambda<Func<int, int>>(
					Expression.Assign(testPropertyExpr, pValueExpr),
					pValueExpr);

			var assignmentExpr = assignExpr.Body as BinaryExpression;
			Debug.Assert(assignmentExpr.Type == typeof(int));
			var propertyExpr = assignmentExpr.Left as MemberExpression;
			Debug.Assert(propertyExpr.Member.Name == "TestProperty");
			Debug.Assert(propertyExpr.Type == typeof(int));
			var valueExpr = assignmentExpr.Right as ParameterExpression;
			Debug.Assert(valueExpr.Name == "value");
			Debug.Assert(valueExpr.Type == typeof(int));

			// 式木を動的にコンパイルする
			Func<int, int> compiledFunc = assignExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == 123);
			Debug.Assert(TestProperty == 123);
#endif
		}

		// プロパティに値を代入させるためのヘルパーメソッド
		public static TValue PropertySetter<TValue>(Action<TValue> setter, TValue newValue)
		{
			setter(newValue);	// デリゲートに設定させる
			return newValue;	// ちゃんと値を返すことで、式の体を成す
		}

		// Expression.Assignを使わずにプロパティ代入
		static void ExpressionAssignForPropertyCompatible()
		{
			TestProperty = -1;

			// パラメータの式木
			var pValueExpr = Expression.Parameter(typeof(int), "value");

			// プロパティを示す式木
			var testProperty = typeof(Program).GetProperty("TestProperty");
			var testPropertyExpr = Expression.Property(null, testProperty);

			// PropertySetterの引数に渡すラムダ式
			// innerValue => TestProperty = innerValue;
			// innerValue => set_TestProperty(innerValue);
			var pInnerValueExpr = Expression.Parameter(typeof(int), "innerValue");
			var setterExpr =
				Expression.Lambda(
					typeof(Action<>).MakeGenericType(testPropertyExpr.Type),
					Expression.Call(
						null,
						testProperty.GetSetMethod(false),	// プロパティのsetterメソッドを取得する
						pInnerValueExpr),
					pInnerValueExpr);

			// PropertySetterのクローズジェネリックメソッド
			var propertySetterMethod = typeof(Program).
				GetMethod("PropertySetter").
				MakeGenericMethod(testPropertyExpr.Type);

			// PropertySetterを呼び出す式木から、ラムダ式木を生成
			var assignExpr =
				Expression.Lambda<Func<int, int>>(
					Expression.Call(propertySetterMethod,
						setterExpr,
						pValueExpr),
					pValueExpr);

			// 式木を動的にコンパイルする
			Func<int, int> compiledFunc = assignExpr.Compile();

			// 出来上がったデリゲートを実行する
			var compiledFuncResult = compiledFunc(123);
			Debug.Assert(compiledFuncResult == 123);
			Debug.Assert(TestProperty == 123);
		}
		#endregion

		#region ExpressionBlock
		static void ExpressionBlock()
		{
			// ブロックを含む式の概念（C#ソースコードとしてコンパイルは不可）
			// Expression<Action<int, int>> blockedExpr = (a, b) =>
			// {
			// 	Console.WriteLine("{0}+{1}", a, b);
			// 	Console.WriteLine("{0}*{1}", a, b);
			// };

			// 動的に式木を構築すれば可能

			// パラメータの式木
			var paExpr = Expression.Parameter(typeof(int), "a");
			var pbExpr = Expression.Parameter(typeof(int), "b");

			// Console.WriteLineのメソッド
			var consoleWriteLineMethod = typeof(Console).
				GetMethod("WriteLine", new[] {typeof(string), typeof(object), typeof(object)});

			// ブロック内のそれぞれの式
			var innerExpr1 = Expression.Call(consoleWriteLineMethod,
				Expression.Constant("{0}+{1}"),
				Expression.Convert(paExpr, typeof(object)),
				Expression.Convert(pbExpr, typeof(object)));
			var innerExpr2 = Expression.Call(consoleWriteLineMethod,
				Expression.Constant("{0}*{1}"),
				Expression.Convert(paExpr, typeof(object)),
				Expression.Convert(pbExpr, typeof(object)));

			var blockExpr = Expression.Lambda<Action<int, int>>(
				Expression.Block(
					innerExpr1,
					innerExpr2),
				paExpr,
				pbExpr);

			// 式木を動的にコンパイルする
			Action<int, int> compiledAction = blockExpr.Compile();

			// 出来上がったデリゲートを実行する
			compiledAction(123, 456);
		}

		// ブロックスコープのテスト
		static void ExpressionBlockLocalScopeExample()
		{
			_fieldValue = -1;

			// フィールドを示す式木
			var fieldValueExpr = Expression.Field(null, typeof(Program).GetField("_fieldValue"));

			// FieldSetterのクローズジェネリックメソッド
			var fieldSetterMethod = typeof(Program).
				GetMethod("FieldSetter").
				MakeGenericMethod(fieldValueExpr.Type);

			// ローカル変数パラメータ
			var localVariablesExpr = Expression.Parameter(typeof(object[]), "localVariables");

			// 代入式
			var assignerExpr = Expression.Call(fieldSetterMethod,
				fieldValueExpr,
				Expression.Convert(
					Expression.ArrayIndex(
						localVariablesExpr,
						Expression.Constant(0)),
					typeof(int)));

			// ラムダ式にしてテスト
			var testExpr = Expression.Lambda<Action<object[]>>(
				assignerExpr,
				localVariablesExpr);
			var compiledAction = testExpr.Compile();

			var testLocalVariables = new object[] {123};
			compiledAction(testLocalVariables);
			Debug.Assert(_fieldValue == 123);
		}
		#endregion

		static void Main(string[] args)
		{
			LambdaToDelegate();
			LambdaToExpression();
			DynamicConstruction();

			ExpressionAssignForField();
			ExpressionAssignForFieldCompatible();

			ExpressionAssignForProperty();
			ExpressionAssignForPropertyCompatible();

			ExpressionBlock();
			ExpressionBlockLocalScopeExample();
		}
	}
}
