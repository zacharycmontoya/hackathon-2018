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
            //string bitCodePath = this.EmitBitCode(expression); // Call translate to compile the expression to LLVM
            // Call something to take the LLVM module and run it through the rest of the pipeline
            // Get the results of the pipeline

            // Temporary work:  Jam the input values into doubles so that we can operate on the data using one shader
            // We are currently assuming we just need to operate on numerical types
            double[] modifiedInput = data.Select(x => Convert.ToDouble(x)).ToArray();
            double[] modifiedInput2 = modifiedInput;
            double[] result = GPUOperations.Operations.Add(null, modifiedInput, modifiedInput2);

            double[][] input = new double[2][];
            input[0] = modifiedInput;
            input[1] = modifiedInput2;

            if(!elementType.Equals(typeof(Double)))
                result = (double[]) ExecuteExpression(expression, input);
                //GPUOperations.Operations.Add(null, modifiedInput, modifiedInput2);
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

        // Temporary work: use input values as doubles so that we can operate on the data using one shader
        private object ExecuteExpression(Expression expression, double[][] input)
        {
            // Emit bitcode
            var visitor = new LLVMBitCodeVisitor<T>(data);
            string bitCodePath = visitor.WalkTree(expression); // Call translate to compile the expression to LLVM

            return ExecutionDispatch(bitCodePath, input, visitor);

        }

        // Temporary work: use input values as doubles so that we can operate on the data using one shader
        private object ExecutionDispatch(string bitCodePath, double[][] input, LLVMBitCodeVisitor<T> visitor)
        {
            Stack<ExpressionNode<T>> opStack = visitor.VisitedOpTypes;
            double[] tempResult = new double[0];
            // Currently only support for a single instruction - refactor to accomodate data-passing pipeline
            while (opStack.Count > 0)
            {
                ExpressionType op = opStack.Pop().op;

                switch (op)
                {
                    case ExpressionType.Add:
                        tempResult = GPUOperations.Operations.Add(bitCodePath, input[0], input[1]);
                        break;
                }
            }

            return tempResult;
        }

    }
}
