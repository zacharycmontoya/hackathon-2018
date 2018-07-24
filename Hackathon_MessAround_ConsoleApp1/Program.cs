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
            IQueryable<Customer> query = db.integers.Where(c => c.City == "Seattle");
            Console.WriteLine($"Query:\n{query}\n");

            //var list = query.ToList(); // TODO implement translation of expression tree to LLVM
        }
    }

    public class NorthWind
    {
        public Query<Customer> integers;

        public NorthWind()
        {
            QueryProvider provider = new GPUQueryProvider(null);
            this.integers = new Query<Customer>(provider);
        }
    }

    public class Customer
    {
        public string City;
    }
}
