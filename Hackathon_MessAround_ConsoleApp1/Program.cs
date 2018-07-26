using GPUCollection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Hackathon_MessAround_ConsoleApp1
{
    class Program
    {
        private const int BufferSize = 256;

        static void Main(string[] args)
        {
            var random = new Random();
            /*
            // Process a bunch of doubles
            double[] doubleData = new double[BufferSize];            
            for (var i = 0; i < BufferSize; i++)
            {
                doubleData[i] = (double)(i + 0.1);
            }

            GPUCollection<double> doubleCollection = new GPUCollection<double>(doubleData);
            IQueryable<double> doubleQuery = doubleCollection.Select(i => i + 3);

            foreach (double x in doubleQuery)
            {
                Console.WriteLine(x);
            }
            */
            // Process a bunch of ints
            int[] intData = new int[BufferSize];
            for (var i = 0; i < BufferSize; i++)
            {
                intData[i] = (int)(i + 0.7);
            }

            GPUCollection<int> intCollection = new GPUCollection<int>(intData);
            IQueryable<int> intQuery = intCollection.Select(i => (i - 3 + 5));

            foreach (int x in intQuery)
            {
                Console.WriteLine(x);
            }
        }
    }
}
