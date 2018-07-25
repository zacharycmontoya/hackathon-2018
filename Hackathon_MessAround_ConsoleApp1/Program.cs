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
            IEnumerable<int> intArguments = args.Select(str => Int32.Parse(str));
            GPUCollection<int> collection = new GPUCollection<int>(intArguments);
            IQueryable<int> query = collection.Select(i => i + 3);
            Console.WriteLine($"Query:\n{query}\n");

            var list = query.ToList(); // TODO implement translation of expression tree to LLVM
        }
    }
}
