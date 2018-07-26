using LLVMSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace GPUCollection
{
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

        public Stack<ExpressionType> VisitedOpTypes
        {
            get;
            private set;
        }

        internal LLVMBitCodeVisitor(IEnumerable<T> data)
        {
            this.data = data;
            VisitedOpTypes = new Stack<ExpressionType>();
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
            this.Visit(b.Left);

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

            LLVMTypeRef[] opParamTypes = new LLVMTypeRef[] { llvmRetType, llvmRetType };
            LLVMTypeRef opRetType = LLVM.FunctionType(llvmRetType, opParamTypes, false);
            LLVMValueRef opFunc = LLVM.AddFunction(module, "op" + functionNumber, opRetType);

            LLVMBasicBlockRef entry = LLVM.AppendBasicBlock(opFunc, "entry");

            LLVMBuilderRef opBuilder = LLVM.CreateBuilder();
            LLVM.PositionBuilderAtEnd(opBuilder, entry);
            LLVMValueRef tmp = GenerateOp(b.NodeType, opBuilder, opFunc);
            LLVM.BuildRet(opBuilder, tmp);

            T[] dataInArray = data.ToArray();
            //if (retType == typeof(Int32) || retType == typeof(Double))
            if (retType == typeof(Int32))
                lastLLVMFunctionCalledFromMain = LLVM.BuildCall(mainBuilder, opFunc, new LLVMValueRef[] { LLVM.ConstInt(llvmRetType, Convert.ToUInt64((object)dataInArray[0]), new LLVMBool(0)), LLVM.ConstInt(llvmRetType, Convert.ToUInt64((object)dataInArray[1]), new LLVMBool(0)) }, "functioncall");
            else
                throw new NotSupportedException(string.Format("The binary operator does not yet support return type '{0}'", retType.Name));

            functionNumber++;

            this.Visit(b.Right);
            return b;
        }

        private LLVMValueRef GenerateOp(ExpressionType expressionType, LLVMBuilderRef opBuilder, LLVMValueRef opFunc)
        {
            string name = "op" + functionNumber + "Result";
            switch (expressionType)
            {
                case ExpressionType.Add:
                    VisitedOpTypes.Push(ExpressionType.Add);
                    return LLVM.BuildAdd(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                case ExpressionType.Subtract:
                    VisitedOpTypes.Push(ExpressionType.Subtract);
                    return LLVM.BuildSub(opBuilder, LLVM.GetParam(opFunc, 0), LLVM.GetParam(opFunc, 1), name);
                default:
                    throw new NotSupportedException(string.Format("The binary operator '{0}' is not supported", expressionType));
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
                    default:
                        // TODO: something meaningful
                        break;
                }
            }

            return c;
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
