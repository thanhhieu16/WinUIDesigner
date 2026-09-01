using NUnit.Framework;
using WinUIDesigner.DesignModel;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class ServiceContainerTests
    {
        interface IFakeService { }
        class FakeService : IFakeService { }

        [Test]
        public void GetServiceReturnsNullWhenNotRegistered()
        {
            var services = new ServiceContainer();
            Assert.IsNull(services.GetService<IFakeService>());
        }

        [Test]
        public void AddThenGetReturnsSameInstance()
        {
            var services = new ServiceContainer();
            var instance = new FakeService();
            services.AddService(typeof(IFakeService), instance);
            Assert.AreSame(instance, services.GetService<IFakeService>());
        }

        [Test]
        public void GetRequiredServiceThrowsWhenMissing()
        {
            var services = new ServiceContainer();
            Assert.Throws<ServiceRequiredException>(() => services.GetRequiredService<IFakeService>());
        }

        [Test]
        public void RunWhenAvailableFiresImmediatelyIfAlreadyRegistered()
        {
            var services = new ServiceContainer();
            var instance = new FakeService();
            services.AddService(typeof(IFakeService), instance);

            IFakeService seen = null;
            services.RunWhenAvailable<IFakeService>(s => seen = s);

            Assert.AreSame(instance, seen);
        }

        [Test]
        public void RunWhenAvailableFiresLaterWhenRegisteredAfterSubscribing()
        {
            var services = new ServiceContainer();
            IFakeService seen = null;
            services.RunWhenAvailable<IFakeService>(s => seen = s);

            Assert.IsNull(seen);

            var instance = new FakeService();
            services.AddService(typeof(IFakeService), instance);

            Assert.AreSame(instance, seen);
        }
    }
}
