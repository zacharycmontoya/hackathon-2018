using SharpDX.Direct3D;
using SharpDX.Direct3D12;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ShaderCompilation = SharpDX.D3DCompiler.ShaderBytecode;

namespace GPUOperations
{
    public unsafe class Operations
    {
        private static Device device;
        private static Fence fence;
        private static CommandQueue commandQueue;
        private static CommandAllocator commandAllocator;
        private static GraphicsCommandList commandList;

        private static long currentFence;
        private static AutoResetEvent fenceEvent;

        private static byte[] AddArrayShaderCode;

        private const int ShaderThreadCount = 128;
        private const int BufferSize = 65536 * ShaderThreadCount;

        public static float[] Add(string objPath)
        {
            using (var fileStream = File.Create("AddArray.dxil"))
            {
                ShaderCompilation.CompileFromFile("AddArray.hlsl", "CSMain", "cs_5_0").Bytecode.Save(fileStream);
            }

            objPath = "AddArray.dxil";

            using (var fileStream = File.OpenRead(objPath))
            {
                AddArrayShaderCode = new byte[fileStream.Length];
                fileStream.Read(AddArrayShaderCode, 0, AddArrayShaderCode.Length);
            }

            device = new Device(null, FeatureLevel.Level_11_0);
            fence = device.CreateFence(0, FenceFlags.None);
            commandQueue = device.CreateCommandQueue(CommandListType.Compute);
            commandAllocator = device.CreateCommandAllocator(CommandListType.Compute);
            commandList = device.CreateCommandList(0, CommandListType.Compute, commandAllocator, null);

            currentFence = 0;
            fenceEvent = new AutoResetEvent(false);

            float[] left = new float[BufferSize];
            float[] right = new float[BufferSize];

            var random = new Random();

            for (var i = 0; i < BufferSize; i++)
            {
                left[i] = (float)(random.NextDouble());
                right[i] = (float)(random.NextDouble());
            }

            var gpuResult = AddGpu(left, right);

            return gpuResult;
        }

        public static float[] AddGpu(float[] left, float[] right)
        {
            if (left.Length != right.Length)
            {
                throw new InvalidOperationException();
            }
            long size = (sizeof(float) * left.Length);

            float[] output = new float[left.Length];

            Resource leftInputBuffer = DirectXHelpers.CreateBuffer(device, size, HeapType.Upload, ResourceFlags.None, ResourceStates.GenericRead);
            Resource rightInputBuffer = DirectXHelpers.CreateBuffer(device, size, HeapType.Upload, ResourceFlags.None, ResourceStates.GenericRead);
            Resource outputBuffer = DirectXHelpers.CreateBuffer(device, size, HeapType.Readback, ResourceFlags.None, ResourceStates.CopyDestination);

            fixed (float* pLeft = &left[0])
            {
                var mappedMemory = leftInputBuffer.Map(0);
                Buffer.MemoryCopy(pLeft, mappedMemory.ToPointer(), size, size);
                leftInputBuffer.Unmap(0);
            }

            fixed (float* pRight = &right[0])
            {
                var mappedMemory = rightInputBuffer.Map(0);
                Buffer.MemoryCopy(pRight, mappedMemory.ToPointer(), size, size);
                rightInputBuffer.Unmap(0);
            }

            var rootParameters = new RootParameter[3]
            {
                new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ShaderResourceView),
                new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ShaderResourceView),
                new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.UnorderedAccessView)
            };
            var rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters);

            RootSignature computeRootSignature = DirectXHelpers.CreateRootSignature(device, rootParameters);
            PipelineState computePipelineState = DirectXHelpers.CreateComputePipelineState(device, computeRootSignature, AddArrayShaderCode);

            //sw.Restart();
            commandList.Close();
            commandAllocator.Reset();
            {
                commandList.Reset(commandAllocator, computePipelineState);
                commandList.SetComputeRootSignature(computeRootSignature);
                commandList.SetComputeRootShaderResourceView(0, leftInputBuffer.GPUVirtualAddress);
                commandList.SetComputeRootShaderResourceView(1, rightInputBuffer.GPUVirtualAddress);
                commandList.SetComputeRootShaderResourceView(2, outputBuffer.GPUVirtualAddress);
                commandList.Dispatch(left.Length / ShaderThreadCount, 1, 1);
                commandList.Close();
            }
            commandQueue.ExecuteCommandList(commandList);
            FlushCommandQueue();
            commandList.Reset(commandAllocator, null);
            //sw.Stop();

            fixed (float* pOutput = &output[0])
            {
                var mappedMemory = outputBuffer.Map(0);
                Buffer.MemoryCopy(mappedMemory.ToPointer(), pOutput, size, size);
                outputBuffer.Unmap(0);
            }

            return output;
        }

        private static void FlushCommandQueue()
        {
            currentFence++;
            commandQueue.Signal(fence, currentFence);

            if (fence.CompletedValue < currentFence)
            {
                fence.SetEventOnCompletion(currentFence, fenceEvent.SafeWaitHandle.DangerousGetHandle());
                fenceEvent.WaitOne();
            }
        }
    }
}
