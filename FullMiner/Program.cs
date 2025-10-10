using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    
    public static void Main(string[] args)
    {
        // Мок последней транзакции (можно заменить на реальную структуру)
        string lastTransaction = "user1->user22:5BTC;timestamp:2025-10-08";

        // Целевой хеш должен начинаться с определённого количества нулей (сложность)
        string targetPrefix = "00000000000000000000";
        var sha256 = SHA256.Create();


        string dataToHash = $"{lastTransaction}:00000000000000000000";
        var buffer = Encoding.UTF8.GetBytes(dataToHash);
        var byteLength = buffer.Length;
        var transactionsDataLenght = lastTransaction.Length;

        // Лог времени старта
        var startTime = DateTime.Now;
        Console.WriteLine($"Старт майнинга: {startTime:HH:mm:ss.fff}");

        long foundNonce = -1;
        string foundHash = "";
        int counter = 0;
        for (long nonce = 0; nonce < long.MaxValue; nonce++)
        {
            if (counter == 1_000_000)
            {
                Console.WriteLine($"Итерация: {nonce} | Время: {DateTime.Now:HH:mm:ss.fff}");
                counter = 0;
            }
            counter++;

            int i = byteLength - 1;
            /*
            while (i > transactionsDataLenght)
            {
                if (buffer[i] < 0x39) //'9'
                {
                    buffer[i]++;
                    break;
                }
                else
                {
                    buffer[i] = 0x30;//'0'
                    i--;
                }
            }
            */
            //unsafe
            //{
            //    fixed (byte* p = buffer)
            //    {
            //        byte* ptr = p + byteLength - 1;
            //        while (ptr >= p + transactionsDataLenght)
            //        {
            //            if (*ptr < 0x39) // '9'
            //            {
            //                (*ptr)++;
            //                break;
            //            }
            //            else
            //            {
            //                *ptr = 0x30; // '0'
            //                ptr--;
            //            }
            //        }
            //    }
            //}

            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{lastTransaction}:{nonce}"));
            //var hash = sha256.ComputeHash(buffer);
            if (hash[0] == 0 && 
                hash[1] == 0 && 
                hash[2] == 0 && 
                hash[3] == 0 )
            {
                foundNonce = nonce;
                foundHash = Convert.ToHexString(hash).ToLowerInvariant();
                break;
            }
        }

        // Лог времени финиша
        var finishTime = DateTime.Now;
        Console.WriteLine($"Финиш майнинга: {finishTime:HH:mm:ss.fff}");

        if (foundNonce != -1)
        {
            Console.WriteLine($"Найден подходящий nonce: {foundNonce}");
            Console.WriteLine($"Хеш: {foundHash}");
        }
        else
        {
            Console.WriteLine("Не удалось найти подходящий nonce.");
        }
    }
}
