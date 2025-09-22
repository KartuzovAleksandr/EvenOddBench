// Дан массив целых чисел размерностью n > 0.
// Выделить четные и отсортировать по возрастанию, нечетные - по убыванию
// Результат записать обратно
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;

BenchmarkRunner.Run<EvenOdd>();

public class EvenOdd
{
    [Params(1_000)]
    public int n;
    private readonly int max = 100;

    [Benchmark]
    public int[] EvenOddA()
    {
        int[] m = new int[n];
        Random r = new();
        for (int i = 0; i < n; i++)
        {
            m[i] = r.Next(max);
        }
        // выделение четных и нечетных
        int[] m2 = new int[n];
        int[] m1 = new int[n];
        int c1 = 0, c2 = 0;
        for (int i = 0; i < n; i++)
        {
            if (m[i] % 2 == 0)
            {
                m2[c2++] = m[i];
            }
            else
            {
                m1[c1++] = m[i];
            }
        }
        /*
        // пузырек по возрастанию для четных
        int temp;
        for (int i = 0; i < c2; i++)
        {
            for (int j = 0; j < c2 - i - 1; j++)
            {
                if (m2[j] > m2[j + 1])
                {
                    temp = m2[j];
                    m2[j] = m2[j + 1];
                    m2[j + 1] = temp;
                }
            }
        }
        // пузырек по убыванию для нечетных
        for (int i = 0; i < c1; i++)
        {
            for (int j = 0; j < c1 - i - 1; j++)
            {
                if (m1[j] < m1[j + 1])
                {
                    temp = m1[j];
                    m1[j] = m1[j + 1];
                    m1[j + 1] = temp;
                }
            }
        }
        */
        QuickSort(false, m2, 0, c2 - 1);
        QuickSort(true, m1, 0, c1 - 1);
        // объединение массивов
        int k = 0;
        for (int i = 0; i < c2; i++)
        {
            m[k++] = m2[i];
        }
        for (int i = 0; i < c1; i++)
        {
            m[k++] = m1[i];
        }
        return m;
    }

    public int[] QuickSort(bool desc, int[] array, int leftIndex, int rightIndex)
    {
        var i = leftIndex;
        var j = rightIndex;
        var pivot = array[leftIndex];
        while (i <= j)
        {
            if (!desc)
            {
                while (array[i] < pivot)
                {
                    i++;
                }
                while (array[j] > pivot)
                {
                    j--;
                }
            }
            else
            {
                while (array[i] > pivot)
                {
                    i++;
                }
                while (array[j] < pivot)
                {
                    j--;
                }
            }
            if (i <= j)
            {
                int temp = array[i];
                array[i] = array[j];
                array[j] = temp;
                i++;
                j--;
            }
        }
        if (leftIndex < j)
            QuickSort(desc, array, leftIndex, j);
        if (i < rightIndex)
            QuickSort(desc, array, i, rightIndex);
        return array;
    }

    [Benchmark]
    public int[] EvenOddAA()
    {
        int[] m = new int[n];
        Random r = new();
        for (int i = 0; i < n; i++)
        {
            m[i] = r.Next(max);
        }
        var m2 = Array.FindAll(m, x => x % 2 == 0);
        var m1 = Array.FindAll(m, x => x % 2 != 0);
        Array.Sort(m2);
        Array.Sort(m1, (x, y) => y.CompareTo(x));
        // m = m1.Concat(m2).ToArray();
        m = [.. m2, .. m1];

        return m;
    }
    [Benchmark]
    public List<int> EvenOddBag()
    {
        Random r = new();
        var l = new ConcurrentBag<int>();
        for (int i = 0; i < n; i++)
        {
            l.Add(r.Next(max));
        }
        ConcurrentBag<int> l2 = new();
        ConcurrentBag<int> l1 = new();
        Parallel.ForEach(l, x =>
        {
            if (x % 2 == 0)
                l2.Add(x);
        });
        Parallel.ForEach(l, x =>
        {
            if (x % 2 != 0)
                l1.Add(x);
        });
        l2.OrderBy(x => x).ToList();
        l1.OrderByDescending(x => x).ToList();
        l = [.. l2, .. l1];
        return l.ToList();
    }

    [Benchmark]
    public List<int> EvenOddC()
    {
        List<int> l = new(n);
        Random r = new();
        for (int i = 0; i < n; i++)
        {
            l.Add(r.Next(max));
        }
        var l2 = new List<int>(l);
        l2.RemoveAll(x => x % 2 != 0);
        var l1 = new List<int>(l);
        l1.RemoveAll(x => x % 2 == 0);
        l2.Sort();
        l1.Sort((x, y) => y.CompareTo(x));
        // m = m1.Concat(m2).ToArray();
        l = [.. l2, .. l1];

        return l;
    }
    [Benchmark]
    public List<int> EvenOddL()
    {
        Random r = new();
        var l = Enumerable.Range(0, n).Select(x => r.Next(max)).ToList();
        //var l = new ConcurrentBag<int>();
        //for (int i = 0; i < n; i++)
        //{
        //    l.Add(r.Next(max));
        //}
        var l2 = from x in l.AsParallel()
                 where x % 2 == 0
                 orderby x
                 select x;
        var l1 = from x in l.AsParallel()
                 where x % 2 != 0
                 orderby x descending
                 select x;
        // l = l2.Concat(l1).ToList();
        l = [.. l2, .. l1];

        return l;
    }
}