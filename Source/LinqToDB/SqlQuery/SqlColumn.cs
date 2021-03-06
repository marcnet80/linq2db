﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace LinqToDB.SqlQuery
{
	public class SqlColumn : IEquatable<SqlColumn>, ISqlExpression
	{
		public SqlColumn(SelectQuery? parent, ISqlExpression expression, string? alias)
		{
			Parent      = parent;
			_expression = expression ?? throw new ArgumentNullException(nameof(expression));
			RawAlias    = alias;

#if DEBUG
			_columnNumber = ++_columnCounter;
#endif
		}

		public SqlColumn(SelectQuery builder, ISqlExpression expression)
			: this(builder, expression, null)
		{
		}

#if DEBUG
		readonly int _columnNumber;
		public   int  ColumnNumber => _columnNumber;
		static   int _columnCounter;
#endif

		ISqlExpression _expression;
		
		public ISqlExpression Expression
		{
			get => _expression;
			set
			{
				if (_expression == value)
					return;
				_expression = value;
				_hashCode   = null;
			}
		}

		SelectQuery? _parent;
		
		public SelectQuery? Parent
		{
			get => _parent;
			set
			{
				if (_parent == value)
					return;
				_parent   = value;
				_hashCode = null;
			}
		}

		internal string?        RawAlias   { get; set; }

		public string? Alias
		{
			get
			{
				if (RawAlias == null)
				{
					switch (Expression)
					{
						case SqlField    field  : return field.Alias ?? field.PhysicalName;
						case SqlColumn   column : return column.Alias;
						case SelectQuery query:
							{
								if (query.Select.Columns.Count == 1 && query.Select.Columns[0].Alias != "*")
									return query.Select.Columns[0].Alias;
								break;
							}
					}
				}

				return RawAlias;
			}
			set => RawAlias = value;
		}

		private bool   _underlyingColumnSet;

		private SqlColumn? _underlyingColumn;

		public  SqlColumn?  UnderlyingColumn
		{
			get
			{
				if (_underlyingColumnSet)
					return _underlyingColumn;

				var columns = new List<SqlColumn>(10);
				var column  = Expression as SqlColumn;

				while (column != null)
				{
					if (column._underlyingColumn != null)
					{
						columns.Add(column._underlyingColumn);
						break;
					}

					columns.Add(column);
					column = column.Expression as SqlColumn;
				}

				_underlyingColumnSet = true;
				if (columns.Count == 0)
					return null;

				_underlyingColumn = columns[columns.Count - 1];

				for (var i = 0; i < columns.Count - 1; i++)
				{
					var c = columns[i];
					c._underlyingColumn    = _underlyingColumn;
					c._underlyingColumnSet = true;
				}

				return _underlyingColumn;
			}
		}

		int? _hashCode;

		[SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
		public override int GetHashCode()
		{
			if (_hashCode.HasValue)
				return _hashCode.Value;

			var hashCode = Parent?.GetHashCode() ?? 0;

			hashCode = unchecked(hashCode + (hashCode * 397) ^ Expression.GetHashCode());
			if (UnderlyingColumn != null)
				hashCode = unchecked(hashCode + (hashCode * 397) ^ UnderlyingColumn.GetHashCode());

			_hashCode = hashCode;

			return hashCode;
		}

		public bool Equals(SqlColumn? other)
		{
			if (other == null)
				return false;

			if (!Equals(Parent, other.Parent))
				return false;

			if (Expression.Equals(other.Expression))
				return true;

			return UnderlyingColumn != null && UnderlyingColumn.Equals(other.UnderlyingColumn);
		}

		public override string ToString()
		{
#if OVERRIDETOSTRING
			var sb  = new StringBuilder();
			var dic = new Dictionary<IQueryElement, IQueryElement>();

			sb
				.Append('t')
				.Append(Parent?.SourceID ?? -1)
#if DEBUG
				.Append('[').Append(_columnNumber).Append(']')
#endif
				.Append(".")
				.Append(Alias ?? "c")
				.Append(" => ");

			Expression.ToString(sb, dic);

			return sb.ToString();

#else
			if (Expression is SqlField)
				return ((IQueryElement)this).ToString(new StringBuilder(), new Dictionary<IQueryElement,IQueryElement>()).ToString();

			return base.ToString()!;
#endif
		}

		#region ISqlExpression Members

		public bool CanBeNull => Expression.CanBeNull;

		public bool Equals(ISqlExpression other, Func<ISqlExpression,ISqlExpression,bool> comparer)
		{
			if (this == other)
				return true;

			if (!(other is SqlColumn otherColumn))
				return false;

			if (Parent != otherColumn.Parent)
				return false;

			if (Parent!.HasSetOperators)
				return false;

			return
				Expression.Equals(
					otherColumn.Expression,
					(ex1, ex2) =>
					{
//							var c = ex1 as Column;
//							if (c != null && c.Parent != Parent)
//								return false;
//							c = ex2 as Column;
//							if (c != null && c.Parent != Parent)
//								return false;
						return comparer(ex1, ex2);
					})
				&&
				comparer(this, other);
		}

		public int   Precedence => SqlQuery.Precedence.Primary;
		public Type? SystemType => Expression.SystemType;

		public ICloneableElement Clone(Dictionary<ICloneableElement, ICloneableElement> objectTree, Predicate<ICloneableElement> doClone)
		{
			if (!doClone(this))
				return this;

			var parent = (SelectQuery?)Parent?.Clone(objectTree, doClone);

			if (!objectTree.TryGetValue(this, out var clone))
				objectTree.Add(this, clone = new SqlColumn(
					parent,
					(ISqlExpression)Expression.Clone(objectTree, doClone),
					RawAlias));

			return clone;
		}

		#endregion

		#region IEquatable<ISqlExpression> Members

		bool IEquatable<ISqlExpression>.Equals(ISqlExpression? other)
		{
			if (this == other)
				return true;

			return other is SqlColumn column && Equals(column);
		}

		#endregion

		#region ISqlExpressionWalkable Members

		public ISqlExpression Walk(WalkOptions options, Func<ISqlExpression,ISqlExpression> func)
		{
			if (!(options.SkipColumns && Expression is SqlColumn))
				Expression = Expression.Walk(options, func)!;

			if (options.ProcessParent)
				Parent = (SelectQuery)func(Parent!);

			return func(this);
		}

		#endregion

		#region IQueryElement Members

		public QueryElementType ElementType => QueryElementType.Column;

		StringBuilder IQueryElement.ToString(StringBuilder sb, Dictionary<IQueryElement,IQueryElement> dic)
		{
			var parentIndex = -1;
			if (Parent != null)
			{
				parentIndex = Parent.Select.Columns.IndexOf(this);
			}

			sb
				.Append('t')
				.Append(Parent?.SourceID ?? - 1)
#if DEBUG
				.Append('[').Append(_columnNumber).Append(']')
#endif
				.Append(".")
				.Append(Alias ?? "c" + (parentIndex >= 0 ? parentIndex + 1 : parentIndex));

			return sb;
		}

		#endregion
	}
}
