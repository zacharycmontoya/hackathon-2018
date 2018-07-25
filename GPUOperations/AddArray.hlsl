struct Data
{
    float v1;
};

StructuredBuffer<Data> LeftInputBuffer : register(t0);
StructuredBuffer<Data> RightInputBuffer : register(t1);
RWStructuredBuffer<Data> OutputBuffer : register(u0);

[numthreads(128, 1, 1)]
void CSMain(int3 DTid : SV_DispatchThreadID)
{
    OutputBuffer[DTid.x].v1 = LeftInputBuffer[DTid.x].v1 + RightInputBuffer[DTid.x].v1;
}
