// Дан массив целых чисел размерностью n > 0.
// Выделить четные и отсортировать по возрастанию, нечетные - по убыванию
// Результат записать обратно

//*************************************************************************************************
//В общем вот вам друзья фейерверк подходов-методов
//Но они подобраны специально под эту задачу фильтрации + сортировки на наборе в 1М
//Оказалось наилучший - фильтрация однопоточная, сортировка встроенным Array.Sort в Parallel.Invoke
//*************************************************************************************************

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
// ILGPU
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

BenchmarkRunner.Run<EvenOdd>();

[WarmupCount(1)]
[IterationCount(3)]
public class EvenOdd
{
    [Params(1_000_000)]
    public int n;

    // установки для случайного набора
    public readonly int max = 5000;
    public List<int>? l; // общая коллекция для всех методов
    public int[]? m;     // общий массив для всех методов

    [GlobalSetup]
    public void Setup()
    {
        var r = new Random(42);
        l = [.. Enumerable.Range(0, n).Select(x => r.Next(max))];
        m = l.ToArray();
    }

    [Benchmark]
    public int[] EvenOddArrayQuick()
    {
        //int[] m = new int[n];
        //Random r = new();
        //for (int i = 0; i < n; i++)
        //{
        //    m[i] = r.Next(max);
        //}
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
        // для результата
        var mresult = new int[n];
        // объединение массивов
        int k = 0;
        for (int i = 0; i < c2; i++)
        {
            mresult[k++] = m2[i];
        }
        for (int i = 0; i < c1; i++)
        {
            mresult[k++] = m1[i];
        }
        return mresult;
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
    public int[] EvenOddArraySort()
    {
        var m2 = Array.FindAll(m, x => x % 2 == 0);
        var m1 = Array.FindAll(m, x => x % 2 != 0);
        Array.Sort(m2);
        // Array.Sort(m1, (x, y) => y.CompareTo(x));
        // сортировка и реверс быстрее CompareTo
        Array.Sort(m1);
        Array.Reverse(m1);
        // результат
        var mresult = new int[n];
        // mresult = m1.Concat(m2).ToArray();
        mresult = [.. m2, .. m1];
        return mresult;
    }

    [Benchmark]
    public int[] EvenOddArrayClone()
    {
        int[] arr = (int[])m.Clone(); // нельзя мутировать оригинал — копируем

        // Разделяем на чётные/нечётные in-place (аналог partition в quicksort)
        int evenCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (arr[i] % 2 == 0)
            {
                // swap to front
                (arr[i], arr[evenCount]) = (arr[evenCount], arr[i]);
                evenCount++;
            }
        }

        // Сортируем чётные (индексы [0, evenCount))
        Array.Sort(arr, 0, evenCount);

        // Сортируем нечётные (индексы [evenCount, n)) и реверсируем
        int oddCount = n - evenCount;
        Array.Sort(arr, evenCount, oddCount);
        Array.Reverse(arr, evenCount, oddCount);

        return arr;
    }

    [Benchmark]
    public int[] EvenOddArraySpanParallelInvokeSort()
    {
        // int[] arr = (int[])m.Clone(); // копия массива, если нельзя мутировать
        // будет немного быстрее через Span<T>
        int[] arr = new int[n];
        m.AsSpan().CopyTo(arr);
        int evenCount = 0;

        // Параллельное разделение — неэффективно, лучше оставить однопоточным
        for (int i = 0; i < n; i++)
        {
            if (arr[i] % 2 == 0)
            {
                (arr[i], arr[evenCount]) = (arr[evenCount], arr[i]);
                evenCount++;
            }
        }

        int oddStart = evenCount;
        int oddCount = n - evenCount;

        // Параллельная сортировка частей
        Parallel.Invoke(
            () => Array.Sort(arr, 0, evenCount),
            () => {
                Array.Sort(arr, oddStart, oddCount);
                Array.Reverse(arr, oddStart, oddCount);
            }
        );

        return arr;
    }

    [Benchmark]
    public List<int> EvenOddParallelForConcurrentBag()
    {
        ConcurrentBag<int> l2 = []; // new();
        ConcurrentBag<int> l1 = []; // new();
        // распараллеливание только для фильтрации
        // но будет очень медленным из-за потокобезопасности ConcurrentBag
        Parallel.ForEach(l, x =>
        {
            if (x % 2 == 0)
                l2.Add(x);
            else
                l1.Add(x);
        });
        // нельзя сортировать ConcurrentBag
        List<int> sortedEven = l2.OrderBy(x => x).ToList();
        List<int> sortedOdd = l1.OrderByDescending(x => x).ToList();
        // результат
        List<int> lresult = new(n);
        lresult = [.. sortedEven, .. sortedOdd];
        return lresult;
    }

    [Benchmark]
    public int[] EvenOddParallelForEach()
    {
        var allEvens = new List<int>();
        var allOdds = new List<int>();
        object lockObj = new(); // для финального слияния

        // правильное использование
        // localInit - Каждый поток создаёт свои местные коллекции
        // body - Пишем в локальных компаниях
        // localFinally - После завершения потока работы — атомарно сливаем в общие списки
        Parallel.ForEach(
            source: l,
            localInit: () => (evens: new List<int>(), odds: new List<int>()),
            body: (item, state, local) =>
            {
                if (item % 2 == 0)
                    local.evens.Add(item);
                else
                    local.odds.Add(item);
                return local;
            },
            localFinally: local =>
            {
                lock (lockObj)
                {
                    allEvens.AddRange(local.evens);
                    allOdds.AddRange(local.odds);
                }
            });

        // Сортируем (однопоточно)
        allEvens.Sort();
        allOdds.Sort();
        allOdds.Reverse();

        // Результат
        var result = new int[n];
        // для скорости сливаем обычными циклами
        int i = 0;
        foreach (var x in allEvens) result[i++] = x;
        foreach (var x in allOdds) result[i++] = x;

        return result;
    }

    [Benchmark]
    public int[] EvenOddParallelForLists()
    {
        int threadCount = Environment.ProcessorCount;
        var evenLists = new List<int>[threadCount];
        var oddLists = new List<int>[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            evenLists[i] = new();
            oddLists[i] = new();
        }

        int chunkSize = (int)Math.Ceiling((double)l.Count / threadCount);

        Parallel.For(0, threadCount, threadIdx =>
        {
            int start = threadIdx * chunkSize;
            int end = Math.Min(start + chunkSize, l.Count);

            for (int i = start; i < end; i++)
            {
                int x = l[i];
                if (x % 2 == 0)
                    evenLists[threadIdx].Add(x);
                else
                    oddLists[threadIdx].Add(x);
            }
        });

        var allEvens = new List<int>();
        var allOdds = new List<int>();

        foreach (var list in evenLists) allEvens.AddRange(list);
        foreach (var list in oddLists) allOdds.AddRange(list);

        allEvens.Sort();
        allOdds.Sort();
        allOdds.Reverse();

        return [.. allEvens, .. allOdds];
    }

    [Benchmark]
    /// <summary>
    /// Разделяет массив на четные и нечетные числа с использованием Parallel.ForEach и Partitioner.
    /// Четные числа сортируются по возрастанию, нечетные - по убыванию.
    /// Результат: четные числа в начале массива, нечетные в конце.
    /// </summary>
    /// <returns>Отсортированный массив по правилу четные(возр.) + нечетные(убыв.)</returns>
    public int[] EvenOddForEachPartitioner()
    {
        // Создаем общие коллекции для хранения результатов всех потоков
        var evens = new List<int>();      // Четные числа (будут отсортированы по возрастанию)
        var odds = new List<int>();       // Нечетные числа (будут отсортированы по убыванию)
        // Объект для синхронизации доступа к общим коллекциям
        var lockObj = new object();       

        // Используем ConcurrentBag для потокобезопасного накопления результатов
        // ConcurrentBag<T> оптимизирован для сценариев, где один поток производит и потребляет,
        // а другие потоки только добавляют (Add) — это как раз наш случай.
        //var evens = new ConcurrentBag<int>();
        //var odds = new ConcurrentBag<int>();

        // Создаем партиционер для равномерного распределения диапазона [0, m.Length) по потокам
        // Partitioner.Create(0, m.Length) автоматически разбивает диапазон на оптимальные куски
        var rangePartitioner = Partitioner.Create(0, m.Length);

        // Параллельная обработка диапазонов с использованием Partitioner
        Parallel.ForEach(rangePartitioner, range =>
        {
            // Локальные коллекции для текущего потока — избегаем обращений к общим коллекциям
            // Это уменьшает конкуренцию и повышает производительность
            var localEvens = new List<int>();
            var localOdds = new List<int>();

            // Последовательная обработка элементов в диапазоне [range.Item1, range.Item2)
            for (int i = range.Item1; i < range.Item2; i++)
            {
                if (m[i] % 2 == 0)
                    localEvens.Add(m[i]);  // Четное число
                else
                    localOdds.Add(m[i]);   // Нечетное число
            }

            // Критическая секция: безопасное объединение локальных результатов с глобальными
            // Lock гарантирует, что только один поток одновременно модифицирует общие коллекции
            lock (lockObj)
            {
                evens.AddRange(localEvens);   // Добавляем четные числа
                odds.AddRange(localOdds);     // Добавляем нечетные числа
            }
            // Добавляем локальные результаты в общие ConcurrentBag-и
            // Add в ConcurrentBag потокобезопасен, блокировок не нужно
            //foreach (int x in localEvens)
            //    evens.Add(x);
            //foreach (int x in localOdds)
            //    odds.Add(x);
            // !!! Но тогда его не сможем Sort - надо кидать обратно в Array или List 
        });

        // Параллельная сортировка двух независимых коллекций
        // Parallel.Invoke запускает действия параллельно, используя пул потоков
        Parallel.Invoke(
            () => evens.Sort(),   // Сортировка четных по возрастанию
            () =>
            {
                odds.Sort();      // Сначала сортируем нечетные по возрастанию
                odds.Reverse();   // Затем разворачиваем для убывания
            });

        // Формируем итоговый массив: четные + нечетные
        var result = new int[n];                    // Предполагается, что n == m.Length
        evens.CopyTo(result, 0);                    // Копируем четные в начало
        odds.CopyTo(result, evens.Count);           // Копируем нечетные после четных
        return result;
    }

    [Benchmark]
    public List<int> EvenOddList()
    {
        //Random r = new();
        //List<int> l = new(n); // capacity
        //for (int i = 0; i < n; i++)
        //{
        //    l.Add(r.Next(max));
        //}
        var l2 = new List<int>(l);
        l2.RemoveAll(x => x % 2 != 0);
        var l1 = new List<int>(l);
        l1.RemoveAll(x => x % 2 == 0);
        l2.Sort();
        l1.Sort((x, y) => y.CompareTo(x));
        // результат
        List<int> lresult = new(n);
        // lresult = (List<int>)l1.Concat(l2);
        lresult = [.. l2, .. l1];
        return lresult;
    }

    [Benchmark]
    //public List<int> EvenOddTasksLists()
    //{
    //    // определяем кол-во процессоров
    //    int taskCount = Environment.ProcessorCount; // или любое другое число
    //    var tasks = new List<Task<(List<int> evens, List<int> odds)>>();

    //    // размер куска (порции)
    //    int chunkSize = (int)Math.Ceiling((double)l.Count / taskCount);
    //    for (int i = 0; i < taskCount; i++)
    //    {
    //        int start = i * chunkSize;
    //        int end = Math.Min(start + chunkSize, l.Count);

    //        tasks.Add(Task.Run(() =>
    //        {
    //            var localEvens = new List<int>();
    //            var localOdds = new List<int>();

    //            for (int j = start; j < end; j++)
    //            {
    //                if (l[j] % 2 == 0)
    //                    localEvens.Add(l[j]);
    //                else
    //                    localOdds.Add(l[j]);
    //            }

    //            return (localEvens, localOdds);
    //        }));
    //    }

    //    // Ждём завершения всех задач
    //    Task.WhenAll(tasks.ToArray());

    //    // Собираем результаты
    //    var allEvens = new List<int>();
    //    var allOdds = new List<int>();

    //    foreach (var task in tasks)
    //    {
    //        var result = task.Result;
    //        allEvens.AddRange(result.evens);
    //        allOdds.AddRange(result.odds);
    //    }

    //    // Сортируем как обычную коллекцию или LINQ
    //    allEvens.Sort(); // или OrderBy(x => x).ToList()
    //    allOdds.Sort((a, b) => b.CompareTo(a)); // или OrderByDescending(x => x).ToList()

    //    // результат
    //    List<int> lresult = new(n);
    //    lresult = [.. allEvens, .. allOdds];
    //    return lresult;
    //}

    // Ключевые изменения с TaskFactory:
    // - Контроль пула потоков - TaskFactory позволяет настраивать параметры ThreadPool
    // - StartNew вместо Run - более явный контроль над созданием задач
    // - Настраиваемые параметры - можно добавить TaskCreationOptions.LongRunning для долгих задач
    // Преимущества TaskFactory:
    // Точная настройка планировщика задач
    // - Возможность отмены через CancellationToken
    // - Лучший контроль над опциями создания задач
    // - Поддержка кастомных TaskScheduler
    public List<int> EvenOddTasksLists()
    {
        // Определяем количество потоков/задач на основе числа процессоров
        int taskCount = Environment.ProcessorCount;
        var tasks = new List<Task<(List<int> evens, List<int> odds)>>();

        // Вычисляем размер порции данных для каждой задачи (равномерное распределение)
        int chunkSize = (int)Math.Ceiling((double)l.Count / taskCount);

        // Создаем TaskFactory для контроля пула потоков (по умолчанию ThreadPool)
        // Создаем TaskFactory с правильным порядком аргументов:
        // 1. CancellationToken (по умолчанию - None)
        // 2. TaskCreationOptions (по умолчанию - None)
        // 3. TaskContinuationOptions (по умолчанию - None)
        // 4. TaskScheduler (по умолчанию - ThreadPool)
        var taskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        // Создаем задачи через TaskFactory.StartNew вместо Task.Run
        for (int i = 0; i < taskCount; i++)
        {
            // Фиксируем значения start/end для захвата в замыкание (closure)
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, l.Count);

            // Используем StartNew<TResult> для функции, возвращающей кортеж
            // Явно указываем TResult — кортеж (List<int>, List<int>)
            Task<(List<int> evens, List<int> odds)> task = taskFactory.StartNew(
                    () =>
                    {
                        // Локальные коллекции для результата текущей задачи
                        var localEvens = new List<int>();
                        var localOdds = new List<int>();

                        // Обрабатываем порцию данных параллельно
                        for (int j = start; j < end; j++)
                        {
                            if (l[j] % 2 == 0)
                                localEvens.Add(l[j]);
                            else
                                localOdds.Add(l[j]);
                        }

                        return (localEvens, localOdds);  // возвращаем кортеж
                    }
                );
            // добавляем задачи
            tasks.Add(task);  // теперь тип совпадает: Task<(List<int>, List<int>)>
        }
        // Ожидаем завершения ВСЕХ задач параллельно
        Task.WhenAll(tasks.ToArray()).Wait();
        // Собираем результаты из всех задач
        var allEvens = new List<int>();
        var allOdds = new List<int>();

        foreach (var task in tasks)
        {
            var (evens, odds) = task.Result;  // Деструктуризация кортежа
            allEvens.AddRange(evens);
            allOdds.AddRange(odds);
        }

        // Сортируем результаты:
        // Четные - по возрастанию
        // Нечетные - по убыванию
        allEvens.Sort();
        allOdds.Sort((a, b) => b.CompareTo(a));

        // Объединяем результаты в финальный список
        return [.. allEvens, .. allOdds];
    }

    [Benchmark]
    public int[] EvenOddTasksArrayPoolParallelInvokeSort()
    {
        int taskCount = Environment.ProcessorCount;
        var tasks = new List<Task<(int[] evens, int countEvens, int[] odds, int countOdds)>>();

        int chunkSize = (int)Math.Ceiling((double)m.Length / taskCount);

        for (int i = 0; i < taskCount; i++)
        {
            int start = i * chunkSize;
            int end = Math.Min(start + chunkSize, l.Count);

            tasks.Add(Task.Run(() =>
            {
                // Берём буферы из пула — вместо List
                var evenBuffer = ArrayPool<int>.Shared.Rent(m.Length / 2); // оценка
                var oddBuffer = ArrayPool<int>.Shared.Rent(m.Length / 2);

                int evenCount = 0, oddCount = 0;

                for (int j = start; j < end; j++)
                {
                    int x = m[j];
                    if (x % 2 == 0)
                        evenBuffer[evenCount++] = x;
                    else
                        oddBuffer[oddCount++] = x;
                }

                return (evenBuffer, evenCount, oddBuffer, oddCount);
            }));
        }

        Task.WhenAll(tasks).Wait();

        // Собираем все чётные и нечётные
        int totalEvens = 0, totalOdds = 0;
        foreach (var task in tasks)
        {
            totalEvens += task.Result.countEvens;
            totalOdds += task.Result.countOdds;
        }

        var allEvens = new int[totalEvens];
        var allOdds = new int[totalOdds];

        int evenOffset = 0, oddOffset = 0;

        foreach (var task in tasks)
        {
            var (evenBuf, ec, oddBuf, oc) = task.Result;

            // Копируем данные
            Array.Copy(evenBuf, 0, allEvens, evenOffset, ec);
            Array.Copy(oddBuf, 0, allOdds, oddOffset, oc);

            evenOffset += ec;
            oddOffset += oc;

            // Возвращаем буферы в пул
            ArrayPool<int>.Shared.Return(evenBuf);
            ArrayPool<int>.Shared.Return(oddBuf);
        }

        // Сортируем параллельно
        Parallel.Invoke(
            () => Array.Sort(allEvens),
            () =>
            {
                Array.Sort(allOdds);
                Array.Reverse(allOdds);
            }
            );

        // Результат
        var result = new int[n];
        Array.Copy(allEvens, 0, result, 0, allEvens.Length);
        Array.Copy(allOdds, 0, result, allEvens.Length, allOdds.Length);

        return result;
    }

    [Benchmark]
    public List<int> EvenOddPLINQ()
    {
        // здесь Parallel.Invoke не даст прироста
        // т.к. AsParallel уже распараллеливает
        // будет быстрее на очень больших данных ~ 1 миллиард
        var l2 = from x in l.AsParallel()
                 where x % 2 == 0
                 orderby x
                 select x;
        var l1 = from x in l.AsParallel()
                 where x % 2 != 0
                 orderby x descending
                 select x;
        // результат
        List<int> lresult = new(n);
        lresult = [.. l2, .. l1];
        return lresult;
    }

    [Benchmark]
    public List<int> EvenOddPLINQDictionary()
    {
        // затраты на работу со словарем и группировкой сильно замедлили код
        var grouped = l.AsParallel()
                       .GroupBy(x => x % 2 == 0)
                       .ToDictionary(g => g.Key, g => g.OrderBy(x => x).ToList());

        var evens = grouped.GetValueOrDefault(true, new List<int>());
        var odds = grouped.GetValueOrDefault(false, new List<int>());
        odds.Reverse();

        return [.. evens, .. odds];
    }

    [Benchmark]
    public List<int> EvenOddLINQParallelInvokeSort()
    {
        // все таки фильтрация лучше без распараллеливания
        var evens = l.Where(x => x % 2 == 0).ToList();
        var odds = l.Where(x => x % 2 != 0).ToList();
        // будет медленнее гораздо
        //var evens = l.AsParallel().Where(x => x % 2 == 0).ToList();
        //var odds = l.AsParallel().Where(x => x % 2 != 0).ToList();

        // наилучшая сортировка - встроенная и эффективно параллелить
        Parallel.Invoke(
            () => evens.Sort(),
            () => {
                odds.Sort();
                odds.Reverse();
            });

        return [.. evens, .. odds];
    }

    [Benchmark]
    public int[] EvenOddSIMD()
    {
        // Копируем массив
        int[] arr = new int[n];
        m.AsSpan().CopyTo(arr);

        // Разделяем на чётные/нечётные in-place
        int evenCount = 0;
        int i = 0;
        int vectorSize = Vector<int>.Count;

        // SIMD-цикл: обрабатываем по вектору
        for (; i <= n - vectorSize; i += vectorSize)
        {
            var v = new Vector<int>(arr, i);

            // Создаём вектор с единицами: [1, 1, 1, ...]
            var one = Vector<int>.One;
            // Побитовое И: v & 1 → 0 для чётных, 1 для нечётных
            var remainder = Vector.BitwiseAnd(v, one);
            // Сравниваем с нулём: remainder == 0 → true для чётных
            var isEven = Vector.Equals(remainder, Vector<int>.Zero);

            // Применяем маску: чётные в начало
            for (int j = 0; j < vectorSize; j++)
            {
                if (isEven[j] != 0)
                {
                    (arr[i + j], arr[evenCount]) = (arr[evenCount], arr[i + j]);
                    evenCount++;
                }
            }
        }

        // Остаток — обычным циклом
        for (; i < n; i++)
        {
            if (arr[i] % 2 == 0)
            {
                (arr[i], arr[evenCount]) = (arr[evenCount], arr[i]);
                evenCount++;
            }
        }

        // Сортируем чётные и нечётные
        Array.Sort(arr, 0, evenCount);
        Array.Sort(arr, evenCount, n - evenCount);
        Array.Reverse(arr, evenCount, n - evenCount);

        return arr;
    }

    [Benchmark]
    //Как это работает
    //Фильтрация (FilterEvenOddKernel)
    //Каждый поток обрабатывает один элемент input[index].
    //Если value & 1 == 0 — чётное, пишем в evens и увеличиваем counts[0] атомарно.
    //Иначе — нечётное, пишем в odds и увеличиваем counts[1].
    //Сортировка
    //RadixSort<int> — эффективная GPU-сортировка.
    //Сортируем evens и odds по возрастанию.
    //Реверс нечётных
    //Ядро ReverseKernel разворачивает odds на месте, чтобы получить убывание.
    //Результат
    //Копируем evens и odds обратно в result:
    //result[0..evenCount] = отсортированные чётные.
    //result[evenCount..] = отсортированные нечётные (по убыванию).
    //Ограничения и советы
    //Требуется CUDA-совместимая видеокарта и установленный CUDA Runtime.
    //Для CPU-тестирования можно заменить CreateCudaAccelerator на CreateCPUAccelerator.
    //При n < 100K ILGPU может быть медленнее из-за накладных расходов на передачу данных GPU ↔ CPU.
    //Для очень больших массивов (1M+) ILGPU покажет максимальный прирост.
    public int[] EvenOddILGPU()
    {
        // Создаём контекст, включающий все ускорители (в т.ч. CUDA)
        using Context context = Context.Create(builder => builder.AllAccelerators());
        // Теперь можно безопасно получить CUDA-устройства
        var cudaDevices = context.GetCudaDevices();
        CudaAccelerator accelerator;
        accelerator = cudaDevices.Count switch
        {
            1 => accelerator = context.CreateCudaAccelerator(0),
            2 => accelerator = context.CreateCudaAccelerator(1),
            _ => throw new InvalidOperationException("Нет CUDA-устройств")
        };
        // создаем акселератор
        //accelerator = context.CreateCudaAccelerator(0);
        // Выделяем буферы на GPU
        using var source = accelerator.Allocate1D<int>(m);
        using var evens = accelerator.Allocate1D<int>(n);
        using var odds = accelerator.Allocate1D<int>(n);
        using var counts = accelerator.Allocate1D<int>(2); // [evenCount, oddCount]

        // Инициализируем счётчики
        counts.MemSetToZero();

        // Фильтрация: чётные в evens, нечётные в odds
        // Загружаем ядро с использованием DefaultStream
        var filterKernel = accelerator.LoadAutoGroupedStreamKernel
            <Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, 
            ArrayView<int>>(FilterEvenOddKernel);
        // Запускаем: extent + аргументы (без stream)
        filterKernel((Index1D)m.Length, source.View, evens.View, odds.View, counts.View);

        // Синхронизация и копирование счётчиков
        accelerator.Synchronize();
        // Читаем счётчики
        var hostCounts = counts.GetAsArray1D();
        int evenCount = hostCounts[0];
        int oddCount = hostCounts[1];

        // Копируем чётные/нечётные на CPU и сортируем там
        // в CUDA не удалось вызвать метод RadixSort (говорят есть)

        // получаем весь массив
        var allEvens = evens.GetAsArray1D();
        var allOdds = odds.GetAsArray1D();

        // используем только первую часть, где есть данные
        var hostEvens = new int[evenCount];
        Array.Copy(allEvens, 0, hostEvens, 0, evenCount);

        var hostOdds = new int[oddCount];
        Array.Copy(allOdds, 0, hostOdds, 0, oddCount);

        Parallel.Invoke(
            () => Array.Sort(hostEvens), // чётные по возрастанию
            () =>
            {
                Array.Sort(hostOdds);
                Array.Reverse(hostOdds); // нечётные по убыванию
            });

        // 5. Собираем результат
        var result = new int[n];
        Array.Copy(hostEvens, 0, result, 0, evenCount);
        Array.Copy(hostOdds, 0, result, evenCount, oddCount);

        return result;
    }

    // Ядро: фильтрация чётных/нечётных
    static void FilterEvenOddKernel(
        Index1D index,
        ArrayView<int> input,
        ArrayView<int> evens,
        ArrayView<int> odds,
        ArrayView<int> counts)
    {
        if (index >= input.Length) return;

        int value = input[index];
        if ((value & 1) == 0) // чётное
        {
            ref int evenCounter = ref counts[0];
            int pos = ILGPU.Atomic.Add(ref evenCounter, 1);
            evens[pos] = value;
        }
        else // нечётное
        {
            ref int oddCounter = ref counts[1];
            int pos = ILGPU.Atomic.Add(ref oddCounter, 1);
            odds[pos] = value;
        }
    }
}