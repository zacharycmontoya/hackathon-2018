using LLVMSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{

    public struct ExpressionNode<T>
    {
        public ExpressionType op;
        public List<T[]> args;
    }

    /// <summary>
    /// Boilerplate code from https://blogs.msdn.microsoft.com/mattwar/2007/07/31/linq-building-an-iqueryable-provider-part-ii/
    /// </summary>
    class LLVMBitCodeVisitor<T> : ExpressionVisitor where T : struct
    {
        private const string ModuleNameString = "GPUCollection";
        private const string BitCodeFilename = "test.bc";

        IEnumerable<T> data;
        LLVMValueRef lastLLVMFunctionCalledFromMain;
        LLVMModuleRef module;
        LLVMBuilderRef mainBuilder;
        int functionNumber;

        // Index in data array
        int currentDataIndex = 0;

        public Stack<ExpressionNode<T>> VisitedOpTypes
        {
            get;
            private set;
        }

        internal LLVMBitCodeVisitor(IEnumerable<T> data)
        {
            this.data = data;
            VisitedOpTypes = new Stack<ExpressionNode<T>>();
        }

        internal string WalkTree(Expression expression)
        {
            InitializeLLVMWriting();
            this.Visit(expression);
            string bitCodeFilePath = FinalizeLLVMWriting();
            return bitCodeFilePath;
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private void InitializeLLVMWriting()
        {
            functionNumber = 0;
            module = LLVM.ModuleCreateWithName(ModuleNameString);
            LLVMTypeRef mainRetType = LLVM.FunctionType(LLVM.Int32Type(), new LLVMTypeRef[0], false);
            LLVMValueRef mainFunc = LLVM.AddFunction(module, "main", mainRetType);
            mainBuilder = LLVM.CreateBuilder();
            LLVMBasicBlockRef mainBlock = LLVM.AppendBasicBlock(mainFunc, "MainEntry");
            LLVM.PositionBuilderAtEnd(mainBuilder, mainBlock);
        }

        /// <summary>
        /// Modifiying https://github.com/paulsmith/getting-started-llvm-c-api/blob/master/sum.c
        /// </summary>
        private string FinalizeLLVMWriting()
        {
            LLVM.BuildRet(mainBuilder, lastLLVMFunctionCalledFromMain);

            string outErrorMessage;
            LLVM.VerifyModule(module, LLVMVerifierFailureAction.LLVMAbortProcessAction, out outErrorMessage);
            string fullFilePath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), BitCodeFilename);
            if (LLVM.WriteBitcodeToFile(module, fullFilePath) != 0)
            {
                throw new IOException($"Unable to write to {fullFilePath}");
            }

            return fullFilePath;
        }

        private static Expression StripQuotes(Expression e)
        {
            while (e.NodeType == ExpressionType.Quote)
            {
                e = ((UnaryExpression)e).Operand;
            }
            return e;
        }

        protected override Expression VisitMethodCall(MethodCallExpression m)
        {
            if (m.Method.DeclaringType == typeof(Queryable) && m.Method.Name == "Where")
            {
                this.Visit(m.Arguments[0]);
                LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                this.Visit(lambda.Body);
                return m;
            }
            else if (m.Method.DeclaringType == typeof(Queryable) && m.Method.Name == "Select")
            {
                LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                this.Visit(lambda.Body);
                return m;
            }

            throw new NotSupportedException(string.Format("The method '{0}' is not supported", m.Method.Name));
        }

        protected override Expression VisitUnary(UnaryExpression u)
        {
            switch (u.NodeType)
            {
                case ExpressionType.Not:
                    // TODO: something meaningful
                    this.Visit(u.Operand);
                    break;
                default:
                    throw new NotSupportedException(string.Format("The unary operator '{0}' is not supported", u.NodeType));
            }

            return u;
        }

        protected override Expression VisitBinary(BinaryExpression b)
        {
            ExpressionNode<T> binaryOpNode = new ExpressionNode<T>();
            binaryOpNode.args = new List<T[]>();

            // Push current node to operation stack
            VisitedOpTypes.Push(binaryOpNode);
            this.Visit(b.Left);

            // Compile function
            T[] dataInArray = data.ToArray();

            // If operand is parameter add to current expresion node
            if (b.Left.NodeType.Equals(ExpressionType.Parameter))
                PopulateNodeWithParameter(ref binaryOpNode, dataInArray);

            // Visit right operand
            this.Visit(b.Right);
            if (b.Right.NodeType.Equals(ExpressionType.Parameter))
                PopulateNodeWithParameter(ref binaryOpNode, dataInArray);

            Type retType = b.Type;
            LLVMTypeRef llvmRetType;

            if (retType == typeof(Int32))
            {
                llvmRetType = LLVM.Int32Type();
            }
            else if (retType == typeof(Double))
            {
                llvmRetType = LLVM.DoubleType();
            }
            else if (retType == typeof(float))
            {
                llvmRetType = LLVM.FloatType();
            }
            else
            {
                throw new NotSupportedException(string.Format("The binary operator does not support operands of type '{0}' is not supported", retType.ToString()));
            }

            binaryOpNode.op = b.NodeType;
            LLVMTypeRef[] opParamTypes = new LLVMTypeRef[] { llvmRetType, llvmRetType };
            LLVMTypeRef opRetType = LLVM.FunctionType(llvmRetType, opParamTypes, false);
            LLVMValueRef opFunc = LLVM.AddFunction(module, "op" + functionNumber, opRetType);

            LLVMBasicBlockRef entry = LLVM.AppendBasicBlock(opFunc, "entry");

            LLVMBuilderRef opBuilder = LLVM.CreateBuilder();
            LLVM.PositionBuilderAtEnd(opBuilder, entry);
            LLVMValueRef tmp = GenerateOp(ref binaryOpNode, opBuilder, opFunc);
            LLVM.BuildRet(opBuilder, tmp);



            //if (retType == typeof(Int32) || retType == typeof(Double))
            if (retType == typeof(Int32))
                lastLLVMFunctionCalledFromMain = LLVM.BuildCall(mainBuilder, opFunc, new LLVMValueRef[] { LLVM.ConstInt(llvmRetType, Convert.ToUInt64(binaryOpNode.args[0][0]), new LLVMBool(0)), LLVM.ConstInt(llvmRetType, Convert.ToUInt64(binaryOpNode.args[1][0]), new LLVMBool(0)) }, "functioncall");
            else
                throw new NotSupportedException(string.Format("The binary operator does not yet support return type '{0}'", retType.Name));

            functionNumber++;

            return b;
        }

        private LLVMValueRef GenerateOp(ref ExpressionNode<T> opNode, LLVMBuilderRef opBuilder, LLVMValueRef opFunc)
        {
            string name = "op" + functionNumber + "Result";

            switch (opNode.op)
            {
                case ExpressionType.Add:


                    return LLVM.BuildAdd(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                case ExpressionType.Subtract:

                    return LLVM.BuildSub(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                default:
                    throw new NotSupportedException(string.Format("The binary operator '{0}' is not supported", opNode.op));
            }

        }

        protected override Expression VisitConstant(ConstantExpression c)
        {
            IQueryable q = c.Value as IQueryable;
            if (q != null)
            {
                // TODO: something meaningful
            }
            else if (c.Value == null)
            {
                // TODO: something meaningful
            }
            else
            {
                switch (System.Type.GetTypeCode(c.Value.GetType()))
                {
                    case TypeCode.Boolean:
                        // TODO: something meaningful
                        break;
                    case TypeCode.String:
                        // TODO: something meaningful
                        // sb.Append(c.Value);
                        break;
                    case TypeCode.Object:
                        throw new NotSupportedException(string.Format("The constant for '{0}' is not supported", c.Value));
                    case TypeCode.Int32:
                        if (typeof(T) == typeof(Int32))
                        {
                            var currentNode = VisitedOpTypes.Peek();
                            currentNode.args.Add(new T[] { (T)c.Value });
                        }
                        break;
                    default:
                        // TODO: something meaningful
                        break;
                }
            }

            return c;
        }

        protected void PopulateNodeWithParameter(ref ExpressionNode<T> binaryOpNode, T[] dataInArray)
        {
            {
                if (currentDataIndex >= dataInArray.Length)
                    throw new Exception();

                // TODO - This assumes data is left-hand operand
                binaryOpNode.args.Add(dataInArray);
            }
        }

        protected override Expression VisitMember(MemberExpression m)
        {
            if (m.Expression != null && m.Expression.NodeType == ExpressionType.Parameter)
            {
                // TODO: something meaningful
                return m;
            }
            throw new NotSupportedException(string.Format("The member '{0}' is not supported", m.Member.Name));
        }
    }
}
