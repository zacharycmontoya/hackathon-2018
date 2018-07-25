using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    public class GPUCollection<T> : BaseQuery<T>
    {
        public GPUCollection(IEnumerable<T> data) : base(new GPUQueryProvider<T>(data))
        {
        }
    }
}
