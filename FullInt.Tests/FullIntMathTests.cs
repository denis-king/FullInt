using FullInt.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using FullIntCore = FullInt.Core.FullInt;

namespace FullInt.Tests
{
    [TestClass]
    public class FullIntMathTests
    {
        [TestMethod]
        public void Add_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(456L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(579L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_NegativeNumbers_ReturnsSum()
        {
            var a = new FullIntCore(-100L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(-150L, toBase.dec), result);
        }

        [TestMethod]
        public void Add_PositiveAndNegative_ReturnsDifference()
        {
            var a = new FullIntCore(200L, toBase.dec);
            var b = new FullIntCore(-50L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(150L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_SubtractsCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(40L, toBase.dec);
            var result = FullIntMath.sub(a, b);
            Assert.AreEqual(new FullIntCore(60L, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_MultipliesCorrectly()
        {
            var a = new FullIntCore(12L, toBase.dec);
            var b = new FullIntCore(11L, toBase.dec);
            var result = FullIntMath.mult(a, b);
            Assert.AreEqual(new FullIntCore(132L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_DividesCorrectly()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(4L, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(25L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Div_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            FullIntMath.div(a, b);
        }

        [TestMethod]
        public void Mod_ReturnsRemainder()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(7L, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(2L, toBase.dec), result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Mod_DivideByZero_Throws()
        {
            var a = new FullIntCore(100L, toBase.dec);
            var b = new FullIntCore(0L, toBase.dec);
            FullIntMath.mod(a, b);
        }

        [TestMethod]
        public void Pow_RaisesToPower()
        {
            var a = new FullIntCore(2L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            var result = FullIntMath.pow(a, b);
            Assert.AreEqual(new FullIntCore(1024L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Positive_ReturnsSame()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var result = FullIntMath.abs(a);
            Assert.AreEqual(new FullIntCore(123L, toBase.dec), result);
        }

        [TestMethod]
        public void Abs_Negative_ReturnsPositive()
        {
            var a = new FullIntCore(-123L, toBase.dec);
            var result = FullIntMath.abs(a);
            Assert.AreEqual(new FullIntCore(123L, toBase.dec), result);
        }

        [TestMethod]
        public void Jb_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(10L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(FullIntMath.jb(a, b));
        }

        [TestMethod]
        public void Jb_A_NotLessThan_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.jb(a, b));
        }

        [TestMethod]
        public void Jb_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.jb(a, b));

            a = new FullIntCore(10L, toBase.dec);
            b = new FullIntCore(10L, toBase.dec);
            Assert.IsFalse(FullIntMath.jb(a, b));
        }

        [TestMethod]
        public void Jbe_LessOrEqual_ReturnsTrue()
        {
            var a = new FullIntCore(5L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            Assert.IsTrue(FullIntMath.jbe(a, b));
            Assert.IsTrue(FullIntMath.jbe(a, new FullIntCore(4L, toBase.dec)));
        }

        [TestMethod]
        public void Je_Equal_ReturnsTrue()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(123L, toBase.dec);
            Assert.IsTrue(FullIntMath.je(a, b));
        }

        [TestMethod]
        public void Je_NotEqual_ReturnsFalse()
        {
            var a = new FullIntCore(123L, toBase.dec);
            var b = new FullIntCore(124L, toBase.dec);
            Assert.IsFalse(FullIntMath.je(a, b));
        }

        [TestMethod]
        public void ToEqualBase_ConvertsBases()
        {
            var a = new FullIntCore(255L, toBase.hex);
            var b = new FullIntCore(255L, toBase.dec);
            var resultBase = FullIntMath.toEqualBase(a, b);
            Assert.AreEqual(a.Base, b.Base);
            Assert.AreEqual(resultBase, a.Base);
        }

        [TestMethod]
        public void DivMod_Byte_DividesAndReturnsRemainder()
        {
            var a = new FullIntCore(123L, toBase.dec);
            byte b = 10;
            var result = FullIntMath.div_mod(a, b);
            Assert.AreEqual(new FullIntCore(12L, toBase.dec), result[0]);
            Assert.AreEqual(new FullIntCore(3L, toBase.dec), result[1]);
        }

        #region bigger data tests
        [TestMethod]
        public void Jb_Long_A_GreaterThan_B_ReturnsTrue()
        {
            var a = new FullIntCore(long.MaxValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue - 1, toBase.dec);
            Assert.IsTrue(FullIntMath.jb(a, b));
        }

        [TestMethod]
        public void Jb_Long_A_LessOrEqual_B_ReturnsFalse()
        {
            var a = new FullIntCore(long.MinValue, toBase.dec);
            var b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(FullIntMath.jb(a, b));

            a = new FullIntCore(long.MaxValue, toBase.dec);
            b = new FullIntCore(long.MaxValue, toBase.dec);
            Assert.IsFalse(FullIntMath.jb(a, b));
        }

        [TestMethod]
        public void Add_Long_PositiveNumbers_ReturnsSum()
        {
            var a = new FullIntCore(9223372036854775800L, toBase.dec);
            var b = new FullIntCore(5L, toBase.dec);
            var result = FullIntMath.add(a, b);
            Assert.AreEqual(new FullIntCore(9223372036854775805L, toBase.dec), result);
        }

        [TestMethod]
        public void Sub_Long_SubtractsCorrectly()
        {
            var a = new FullIntCore(long.MaxValue, toBase.dec);
            var b = new FullIntCore(1L, toBase.dec);
            var result = FullIntMath.sub(a, b);
            Assert.AreEqual(new FullIntCore(long.MaxValue - 1, toBase.dec), result);
        }

        [TestMethod]
        public void Mult_Long_MultipliesCorrectly()
        {
            var a = new FullIntCore(1234567890L, toBase.dec);
            var b = new FullIntCore(987654321L, toBase.dec);
            var result = FullIntMath.mult(a, b);
            Assert.AreEqual(new FullIntCore(1219326311126352690L, toBase.dec), result);
        }

        [TestMethod]
        public void Div_Long_DividesCorrectly()
        {
            var a = new FullIntCore(9223372036854775806L, toBase.dec);
            var b = new FullIntCore(2L, toBase.dec);
            var result = FullIntMath.div(a, b);
            Assert.AreEqual(new FullIntCore(4611686018427387903L, toBase.dec), result);
        }

        [TestMethod]
        public void Mod_Long_ReturnsRemainder()
        {
            var a = new FullIntCore(9223372036854775806L, toBase.dec);
            var b = new FullIntCore(123456789L, toBase.dec);
            var result = FullIntMath.mod(a, b);
            Assert.AreEqual(new FullIntCore(108215406L, toBase.dec), result);
        }
        #endregion
    }
}