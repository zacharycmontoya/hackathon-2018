using GPUCollection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Hackathon_MessAround_ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            NorthWind db = new NorthWind();
            IQueryable<int> query = db.integers.Select(i => i + 3);
            Console.WriteLine($"Query:\n{query}\n");

            var list = query.ToList(); // TODO implement translation of expression tree to LLVM
        }
    }

    public class NorthWind
    {
        public BaseQuery<int> integers;

        public NorthWind()
        {
            BaseQueryProvider provider = new GPUQueryProvider(null);
            this.integers = new BaseQuery<int>(provider);
        }
    }
}
