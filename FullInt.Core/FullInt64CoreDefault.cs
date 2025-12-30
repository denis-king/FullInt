using System;

namespace FullInt.Core
{
    public class FullInt64CoreDefault : IFullIntCore
    {
        private static toBase Base = toBase.hex;

        private void Normalize3Words(ref ulong d0, ref ulong d1, ulong d2, ref ulong u0, ref ulong u1, ulong u2)
        {
            throw new NotImplementedException();
        }

        private ulong FindDivisionCandidate(Span<ulong> partDividend, Span<ulong> divisor, toBase Base)
        {
            ulong hiDividend;
            ulong loDividend;

            ulong result;
            ulong resultMin;
            ulong resultMax;

            divisor = FullHelper.DeleteZeros(divisor);
            partDividend = FullHelper.DeleteZeros(partDividend);
            
            
            // --- подготовка делителя ---
            ulong D0, D1, D2;
            if (divisor.Length >= 3)
            {
                D0 = divisor[0];
                D1 = divisor[1];
                D2 = divisor[2];
            }
            else if (divisor.Length == 2)
            {
                D0 = divisor[0];
                D1 = divisor[1];
                D2 = 0UL;
            }
            else if (divisor.Length == 1)
            {
                D0 = divisor[0];
                D1 = 0UL;
                D2 = 0UL;
            }
            else
            {
                // нет ни одного слова делителя — в твоей логике это недопустимо
                throw new ArgumentException("Divisor must contain at least one word");
            }

            // --- подготовка делимого ---
            ulong U0, U1, U2;
            if (partDividend.Length >= 3)
            {
                U0 = partDividend[0];
                U1 = partDividend[1];
                U2 = partDividend[2];
            }
            else if (partDividend.Length == 2)
            {
                U0 = partDividend[0];
                U1 = partDividend[1];
                U2 = 0UL;
            }
            else if (partDividend.Length == 1)
            {
                U0 = partDividend[0];
                U1 = 0UL;
                U2 = 0UL;
            }
            else
            {
                U0 = 0UL; U1 = 0UL; U2 = 0UL;
            }
            if (Base == toBase.dec)
            {
                //todo придумать десятичную нормализацию методом сдвигов десятками умножение на степень 10 
                //это дороже но пусть будет для совместимости и дебага
            }
            else
            {
                
            }

            Normalize3Words(ref D0, ref D1, D2, ref U0, ref U1, U2);
            
            //firs step
            if (U0 < D0)
            {
                hiDividend = U0;
                loDividend = U1;
                
                ////dec correction
                //if (Base == toBase.dec)
                //{
                //    //byte carry = 0;
                //    loDividend = Mul128(hiDividend, FullInt64.maxDecCapasity, out hiDividend) + loDividend;
                //    //hiDividend += carry;
                //}
            }
            else
            {
                hiDividend = 0;
                loDividend = U0;
            }
            resultMin = Div128(hiDividend, loDividend, D0 + 1, out _);
            resultMax = Div128(hiDividend, loDividend, D0, out _);

            result = resultMax;

            if (resultMin == resultMax)
            {
                return result;
            }

            //step 2
           
            var range = resultMax - resultMin + 1;
            var step = (Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue) / range;
            result -= D1 / step;

            return result;
            
        }

        protected virtual void Mult128(ulong x, ulong y, bool isDec, out ulong lo, out ulong hi)
        {
            const ulong mask32 = 0xFFFFFFFF;

            ulong x0 = x & mask32;
            ulong x1 = x >> 32;
            ulong y0 = y & mask32;
            ulong y1 = y >> 32;

            ulong z0 = x0 * y0;
            ulong z1 = x0 * y1;
            ulong z2 = x1 * y0;
            ulong z3 = x1 * y1;

            ulong mid1 = z1 + (z0 >> 32);
            ulong mid2 = z2 + (mid1 & mask32);

            hi = z3 + (mid1 >> 32) + (mid2 >> 32);
            lo = (mid2 << 32) | (z0 & mask32);
        }
		
        public void Multiply(ulong a, ulong b, bool isDec, out ulong low, out ulong high)
        {
            high = 0;
            low = 0;
           
			Mult128(a, b, isDec, out low, out high);

            
            if (isDec)
            {
                high = Div128(high, low, FullInt64.maxDecCapasity, out low);
            }
        }

        protected virtual ulong Div128(ulong hi, ulong lo, ulong divisor, out ulong remainder)
        {
            throw new NotImplementedException();
        }
        protected virtual ulong Adc64(ulong a, ulong b, ref byte carry)
        {
            //save adding two ulongs and carry
            var sum1 = a + b;
            bool carry1 = sum1 < a || sum1 < b;
            var sum = sum1 + carry;
            carry1 |= sum < sum1;
            carry = (byte)( carry1 ? 1 : 0);
            return sum;
        }
        protected virtual ulong Sbb64(ulong a, ulong b, ref byte borrow) 
        {
            ulong sum = 0;
            ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            if (a < b + borrow)
            {
                sum = baseMaxValue - b - borrow + a + 1;
                borrow = 1;
            }
            else
            {
                sum = a - b - borrow;
                borrow = 0;
            }
            return sum;
        }
        public ulong Add(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            ulong sum;
            ulong b1, b2;
            //ulong baseMaxValue = Base == toBase.dec ? FullInt64.maxDecValue : ulong.MaxValue;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var resultLastIndex = result.Length - 1;

            byte carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < value1.Length) { b1 = value1[value1LastIndex - i]; } else { b1 = 0; }
                if (i < value2.Length) { b2 = value2[value2LastIndex - i]; } else { b2 = 0; }


                
                sum = Adc64(b1, b2, ref carry);

                //decimal corrections
                if (Base == toBase.dec)
                {
                    carry = (byte)Div128(carry, sum, FullInt64.maxDecCapasity, out sum);
                    //if (carry == 1)
                    //{
                        
                    //}
                    //else if (sum > FullInt64.maxDecValue)
                    //{
                    //    sum = sum - FullInt64.maxDecCapasity;
                    //    carry = 1;
                    //}
                }
                
                result[resultLastIndex - i] = sum;
            }
            return carry;
        }

        public void Div(Span<ulong> dividend, Span<ulong> divisor, Span<ulong> quotient, Span<ulong> remainder)
        {
            if (Compare(divisor, stackalloc ulong[] {0}) == 0)
            {
                throw new System.DivideByZeroException();
            }

            int bytesIndex = 0;
            bool isInit = true;

            Span<ulong> partDividend = remainder.Slice(1);

            Span<ulong> partResult = stackalloc ulong[] { 0UL };

            Span<ulong> divMult = divisor.Length <= 1024
                ? stackalloc ulong[partDividend.Length]
                : new ulong[partDividend.Length];

            

            var remainderSing = false;
            //пропуск старших нулей операндов
            int zeros1 = 0, zeros2 = 0; //todo use span slice to awoid zeros
            dividend = FullHelper.DeleteZeros(dividend);
            divisor = FullHelper.DeleteZeros(divisor);

            if (isInit)
            {
                if (dividend.Length >= divisor.Length)
                {
                    partDividend = dividend.Slice(0, divisor.Length);
                    zeros1 += divisor.Length;
                    if (Compare(partDividend, divisor) == -1)
                    {
                        partDividend = dividend.Slice(zeros1 - divisor.Length, divisor.Length + 1);
                        zeros1++;
                    }
                }
                else
                {
                    partDividend = dividend;
                    zeros1 += dividend.Length;
                }
            }


            while (dividend.Length - zeros1 > 0 || isInit)
            {

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (!isInit && Compare(partDividend, divisor) == -1)
                {
                    if (bytesIndex > 0)
                    {
                        quotient[bytesIndex++] = 0;//!!!!!!!!!!!
                    }
                }
                else
                {
                   

                    var candidate = FindDivisionCandidate(partDividend, divisor, Base);
                    partResult[0] = candidate;

                    #region remainder = partDividend - (divisor * partResult)
                    //divMult = |value2| * partResult
                    Mult(divisor, partResult, divMult);
                    //remainder = divs - divMult
                    var compare = Sub(partDividend, divMult, remainder);
                    remainderSing = compare < 0;
                    #endregion


                    while (remainderSing || (compare = Compare(remainder, divisor)) >= 0)
                    {
                        //candidate correction
                        compare = Sub(remainder, divisor, remainder);
                        if (remainderSing)
                        {
                            partResult[0]--;
                            remainderSing = compare > 0;
                            if (!remainderSing)
                            {
                                ////it's just for the debug
                                //var diff = candidate - partResult[0];
                                //candidate = FindDivisionCandidate(partDividend, divisor);
                                break;
                            }
                        }
                        else
                        {
                            partResult[0]++;
                        }
                    }
                   
                    if (isInit)
                    {
                        isInit = false;
                        partDividend = remainder.Slice(1);
                    }

                    quotient[bytesIndex++] = partResult[0];
                }
                //если есть еще байти из делимого числа
                if (dividend.Length - zeros1 >= 1)
                {
                    //move divs left and add new byte
                    for (int i = 0; i < partDividend.Length - 1; i++)
                    {
                        partDividend[i] = partDividend[i + 1];
                    }
                    partDividend[partDividend.Length - 1] = dividend[zeros1];

                    zeros1 += 1;
                }
                else
                {
                    break;
                }
            }

            

            //move result to the riht buffer end
            if (bytesIndex < quotient.Length)
            {
                quotient = quotient.Slice(0, bytesIndex);
            }
        }
		
		public void Div(Span<ulong> dividend, ulong divisor, Span<ulong> quotient, out ulong remainder)
        {
			if (divisor == 0)
            {
                throw new System.DivideByZeroException();
            }
            Span<ulong> divisorBuf = stackalloc ulong[] { divisor };
            Span<ulong> divs = stackalloc ulong[2];
            int bytesIndex = 0;
            
            //пропуск старших нулей операндов
            int zeros1 = FullHelper.CountZeros(dividend);

            while (dividend.Length - zeros1 > 0)
            {
                //если есть еще байти из делимого числа
                if (dividend.Length - zeros1 >= 1)
                {
                    //move divs left and add new byte
                    divs[0] = divs[1];
                    divs[1] = dividend[zeros1];
                    zeros1++;
                }
                else
                {
                    break;
                }

                //если делимое меньше частичного делителя, то добавляем к нему еще один байт
                if (divs[0] == 0 && divs[1] < divisor)
                {
                    quotient[bytesIndex++] = 0;//!!!!!!!!!!!
                }
                else
                {
                    ulong b;

                    //128long / long div
                    quotient[bytesIndex++] = Div128(divs[divs.Length - 2], divs[divs.Length - 1], divisor, out b);
                    divs[0] = 0;
                    divs[1] = b;
                }
            }

            //move result to the riht buffer end
            if(bytesIndex < quotient.Length)
            {
                quotient = quotient.Slice(0, bytesIndex);
            }
            remainder = divs[1];
		}

        public void Mult(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            ulong multA, multB = 0;
            ulong multHi, multLo = 0;
            ulong addHi = 0, addMid = 0, addLo = 0;
            var value1LastIndex = value1.Length - 1;
            var value2LastIndex = value2.Length - 1;
            var value1Index = 0;
            var value1ShiftedIndex = 0;
            var resultLastIndex = result.Length - 1;
            byte carry = 0;

            for (int i = 0; i < result.Length; i++)
            {
                value1Index = value1LastIndex - i;
                value1ShiftedIndex = value1Index;
                for (int j = 0; j < value2.Length; j++)
                {
                   
                    if (value1ShiftedIndex >= 0 && value1ShiftedIndex < value1.Length)
                    {
                        multA = value1[value1ShiftedIndex];
                        multB = value2[value2LastIndex - j];
                        if (multA == 0 || multB == 0)
                        {
                            continue;
                        }
                        Multiply(multA, multB, Base == toBase.dec, out multLo, out multHi);
                        
                        //sum = sum + mult
                        carry = 0;
                        addLo = Adc64(addLo, multLo, ref carry);
                        addMid = Adc64(addMid, multHi, ref carry);
                        addHi += carry;
                    }
                    value1ShiftedIndex++;
                }

                result[resultLastIndex - i] = addLo;
                //свиг суммы вправо на 1 байт
                addLo = addMid;
                addMid = addHi;
                addHi = 0;
            }
        }

        public int Sub(Span<ulong> value1, Span<ulong> value2, Span<ulong> result)
        {
            Span<ulong> valueG;
            Span<ulong> valueL;

            //find the greate and less values
            var compareResult = Compare(value1, value2);

            if (compareResult > 0)
            {
                valueG = value1;
                valueL = value2;
            }
            else if (compareResult < 0)
            {
                valueG = value2;
                valueL = value1;
            }
            else
            {
                //prepare the zero result
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = 0;
                }
                return compareResult;
            }
            
            ulong sum;
            ulong b1, b2 = 0;
           
            byte borrow = 0;
            var valueGLastIndex = valueG.Length - 1;
            var valueLLastIndex = valueL.Length - 1;
            var resultLastIndex = result.Length - 1;

            for (int i = 0; i < result.Length; i++)
            {
                if (i < valueG.Length) { b1 = valueG[valueGLastIndex - i]; } else { b1 = 0; }
                if (i < valueL.Length) { b2 = valueL[valueLLastIndex - i]; } else { b2 = 0; }

                sum = Sbb64(b1, b2, ref borrow);

                //dec correction
                if(Base == toBase.dec && borrow == 1)
                {
                    sum -= (ulong.MaxValue - FullInt64.maxDecValue);
                }
                //if (b1 < b2 + borrow)
                //{
                //    sum = baseMaxValue - b2 - borrow + b1 + 1 - long + dec);
                //    borrow = 1;
                //}
                //else
                //{
                //    sum = b1 - b2 - borrow;
                //    borrow = 0;
                //}
                result[resultLastIndex - i] = sum;
            }
			return compareResult;
        }
		
		public int Compare(Span<ulong> value1, Span<ulong> value2)
		{
			int result = 0;
            //toEqualBase(ref value1, ref value2);

            var zeros1 = FullHelper.CountZeros(value1);
            var zeros2 = FullHelper.CountZeros(value2);
            var value1Length = value1.Length - zeros1;
            var value2Length = value2.Length - zeros2;

            
            //comparison by length
            if (value1Length < value2Length)
            {
                result = -1;
            }
            else if (value1Length > value2Length)
            {
                result = 1;
            }
            else
            {
                //comparison by value
                for (int i = 0; i < value1Length; i++)
                {
                    if (value1[i + zeros1] < value2[i + zeros2])
                    {
                        result = -1;
                        break;
                    }
                    else if (value1[i + zeros1] > value2[i + zeros2])
                    {
                        result = 1;
                        break;
                    }
                }
            }
                
            return result;
		}
    }
}