using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/30/linq-building-an-iqueryable-provider-part-i/
    /// </summary>
    public class BaseQuery<T> : IOrderedQueryable<T>
    {
        private BaseQueryProvider queryProvider;
        public IQueryProvider Provider {
            get { return this.queryProvider; }
        }

        public Expression Expression { get; private set; }
        public Type ElementType
        {
            get { return typeof(T); }
        }

        public BaseQuery(BaseQueryProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            queryProvider = provider;
            Expression = Expression.Constant(this);
        }

        public BaseQuery(BaseQueryProvider provider, Expression expression)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (expression == null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            if (!typeof(IQueryable<T>).IsAssignableFrom(expression.Type))
            {
                throw new ArgumentOutOfRangeException(nameof(expression));
            }

            queryProvider = provider;
            Expression = expression;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)queryProvider.Execute(Expression)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)queryProvider.Execute(Expression)).GetEnumerator();
        }

        public override string ToString()
        {
            return queryProvider.GetQueryText(Expression);
        }
    }
}
