using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/31/linq-building-an-iqueryable-provider-part-ii/
    /// </summary>
    public class GPUQueryProvider<T> : BaseQueryProvider where T : struct
    {
        private IEnumerable<T> data;

        public GPUQueryProvider(IEnumerable<T> data)
        {
            this.data = data;
        }

        public override object Execute(Expression expression)
        {
            Type elementType = TypeSystem.GetElementType(expression.Type);
            string bitCodePath = this.EmitBitCode(expression); // Call translate to compile the expression to LLVM
            // Call something to take the LLVM module and run it through the rest of the pipeline
            // Get the results of the pipeline

            // Temporary work:  Jam the input values into doubles so that we can operate on the data using one shader
            // We are currently assuming we just need to operate on numerical types
            double[] modifiedInput = data.Select(x => Convert.ToDouble(x)).ToArray();
            double[] modifiedInput2 = modifiedInput;

            double[] result = GPUOperations.Operations.Add(null, modifiedInput, modifiedInput2);
            if (typeof(T) == typeof(int))
            {
                return Array.ConvertAll(result, x => (int)x);
            }
            else if (typeof(T) == typeof(double))
            {
                return result;
            }
            else
            {
                return result;
            }
        }

        private string EmitBitCode(Expression expression)
        {
            return new LLVMBitCodeVisitor().WalkTree(expression);
        }
    }
}
